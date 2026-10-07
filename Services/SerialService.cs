using System.Globalization;
using System.IO.Ports;
using Microsoft.Extensions.Options;
using PcsHmi.Models;

namespace PcsHmi.Services;

/// <summary>
/// Leser linjer fra Arduinoen, oppdaterer tilstand, alarmer og database,
/// og sender kommandoer (SET/DIM/MODE) tilbake.
///
/// Arduinoen restarter hver gang porten åpnes og starter da med
/// standardverdier. Etter første gyldige linje sendes derfor siste
/// lagrede SET/DIM/MODE på nytt fra databasen.
/// </summary>
public class SerialService : BackgroundService
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly PcsOptions _opt;
    private readonly PlantState _state;
    private readonly AlarmService _alarms;
    private readonly Database _db;
    private readonly ILogger<SerialService> _log;

    private readonly object _writeLock = new();
    private SerialPort? _port;

    private DateTime _lastData = DateTime.MinValue;
    private DateTime _lastLogged = DateTime.MinValue;
    private (double sp, int dim, string mode)? _savedSettings;

    public SerialService(IOptions<PcsOptions> options, PlantState state, AlarmService alarms,
                         Database db, ILogger<SerialService> log)
    {
        _opt = options.Value;
        _state = state;
        _alarms = alarms;
        _db = db;
        _log = log;
    }

    // ------------------------------------------------------------ kommandoer fra HMI
    public bool SetSetpoint(double celsius) =>
        Send("SET:" + Math.Clamp(celsius, 5, 40).ToString("0.0", Inv));   // punktum, ikke komma

    public bool SetDim(int value) => Send("DIM:" + Math.Clamp(value, 0, 255).ToString(Inv));

    public bool SetMode(bool auto) => Send(auto ? "MODE:AUTO" : "MODE:MAN");

    private bool Send(string command)
    {
        lock (_writeLock)
        {
            if (_port is not { IsOpen: true }) return false;
            try
            {
                _port.Write(command + "\n");
                _log.LogInformation("Sendt: {Cmd}", command);
                return true;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Kunne ikke sende {Cmd}", command);
                return false;
            }
        }
    }

    // ------------------------------------------------------------ hovedløkke
    protected override Task ExecuteAsync(CancellationToken ct) =>
        // Egen tråd fordi SerialPort.ReadLine blokkerer
        Task.Factory.StartNew(() => Run(ct), ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private void Run(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var port = new SerialPort(_opt.PortName, _opt.BaudRate)
                {
                    NewLine = "\n",
                    ReadTimeout = 1000,
                    DtrEnable = true
                };
                port.Open();
                lock (_writeLock) _port = port;
                _log.LogInformation("Åpnet {Port}", _opt.PortName);

                _lastData = DateTime.UtcNow;   // gi Arduinoen tid til å starte
                bool restored = false;
                DateTime syncSettingsAfter = DateTime.MaxValue;

                while (!ct.IsCancellationRequested)
                {
                    string line;
                    try
                    {
                        line = port.ReadLine();
                    }
                    catch (TimeoutException)
                    {
                        CheckCommTimeout();
                        continue;
                    }

                    var now = DateTime.UtcNow;
                    var m = LineParser.TryParse(line, now);
                    if (m is null) continue;   // halv linje, WARN o.l.

                    _lastData = now;
                    _alarms.SetCommOk(true);

                    if (!restored)
                    {
                        RestoreSettings();
                        restored = true;
                        // Ikke lagre Arduinoens standardverdier før gjenopprettingen har slått inn
                        syncSettingsAfter = now.AddSeconds(3);
                    }
                    else if (now > syncSettingsAfter)
                    {
                        SaveSettingsIfChanged(m);   // fanger også endringer fra encoder/knapp
                    }

                    _state.Update(m);
                    _alarms.Evaluate(m);

                    if (now - _lastLogged >= TimeSpan.FromSeconds(_opt.LogIntervalSeconds))
                    {
                        _db.InsertMeasurement(m);
                        _lastLogged = now;
                    }
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _log.LogWarning("Serieport {Port}: {Msg}. Prøver igjen om 5 s.", _opt.PortName, ex.Message);
                _alarms.SetCommOk(false);
            }
            finally
            {
                lock (_writeLock) _port = null;
                _state.SetConnected(false);
            }

            ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(5));
        }
    }

    private void CheckCommTimeout()
    {
        if (DateTime.UtcNow - _lastData > TimeSpan.FromSeconds(_opt.CommTimeoutSeconds))
        {
            _state.SetConnected(false);
            _alarms.SetCommOk(false);
        }
    }

    // ------------------------------------------------------------ innstillinger
    private void RestoreSettings()
    {
        var sp = _db.GetSetting("setpoint");
        var dim = _db.GetSetting("dim");
        var mode = _db.GetSetting("mode");

        if (sp != null && double.TryParse(sp, NumberStyles.Float, Inv, out var spVal)) SetSetpoint(spVal);
        if (dim != null && int.TryParse(dim, NumberStyles.Integer, Inv, out var dimVal)) SetDim(dimVal);
        if (mode != null) SetMode(mode == "AUTO");

        if (sp != null && dim != null && mode != null)
            _savedSettings = (double.Parse(sp, Inv), int.Parse(dim, Inv), mode);
    }

    private void SaveSettingsIfChanged(Measurement m)
    {
        var current = (m.Setpoint, m.Dim, m.Mode);
        if (_savedSettings == current) return;

        _db.SetSetting("setpoint", m.Setpoint.ToString("0.0", Inv));
        _db.SetSetting("dim", m.Dim.ToString(Inv));
        _db.SetSetting("mode", m.Mode);
        _savedSettings = current;
    }
}
