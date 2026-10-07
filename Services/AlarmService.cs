using System.Globalization;
using Microsoft.Extensions.Options;
using PcsHmi.Models;

namespace PcsHmi.Services;

/// <summary>
/// Temperaturalarm (høy/lav) med hysterese, og kommunikasjonsalarm.
/// En alarm vises i HMI-en til den er både avsluttet og kvittert.
/// </summary>
public class AlarmService
{
    public const string TempHigh = "TEMP_HIGH";
    public const string TempLow  = "TEMP_LOW";
    public const string Comm     = "COMM";

    private readonly Database _db;
    private readonly PlantState _state;
    private readonly PcsOptions _opt;
    private readonly object _lock = new();
    private readonly Dictionary<string, long> _active = new();   // type -> alarm-id

    public IReadOnlyList<AlarmRecord> OpenAlarms { get; private set; } = Array.Empty<AlarmRecord>();
    public double HighLimit => _opt.HighTempLimitC;
    public double LowLimit => _opt.LowTempLimitC;

    public AlarmService(Database db, PlantState state, IOptions<PcsOptions> options)
    {
        _db = db;
        _state = state;
        _opt = options.Value;

        // Alarmer som var aktive da programmet sist stoppet
        foreach (var a in _db.GetOpenAlarms().Where(a => a.Active))
        {
            if (!_active.TryAdd(a.Type, a.Id)) _db.EndAlarm(a.Id, DateTime.UtcNow);
        }
        Refresh();
    }

    public void Evaluate(Measurement m)
    {
        var h = _opt.AlarmHysteresisC;

        if (m.T1 > _opt.HighTempLimitC)
            Raise(TempHigh, $"Høy temperatur: {Fmt(m.T1)} °C (grense {Fmt(_opt.HighTempLimitC)} °C)", m.T1);
        else if (m.T1 < _opt.HighTempLimitC - h)
            Clear(TempHigh);

        if (m.T1 < _opt.LowTempLimitC)
            Raise(TempLow, $"Lav temperatur: {Fmt(m.T1)} °C (grense {Fmt(_opt.LowTempLimitC)} °C)", m.T1);
        else if (m.T1 > _opt.LowTempLimitC + h)
            Clear(TempLow);
    }

    public void SetCommOk(bool ok)
    {
        if (ok) Clear(Comm);
        else Raise(Comm, "Ingen data fra Arduino", null);
    }

    public void Acknowledge(long id)
    {
        lock (_lock)
        {
            _db.AcknowledgeAlarm(id);
            Refresh();
        }
        _state.Notify();
    }

    private void Raise(string type, string message, double? value)
    {
        lock (_lock)
        {
            if (_active.ContainsKey(type)) return;
            _active[type] = _db.InsertAlarm(type, message, value, DateTime.UtcNow);
            Refresh();
        }
        _state.Notify();
    }

    private void Clear(string type)
    {
        lock (_lock)
        {
            if (!_active.Remove(type, out var id)) return;
            _db.EndAlarm(id, DateTime.UtcNow);
            Refresh();
        }
        _state.Notify();
    }

    private void Refresh() => OpenAlarms = _db.GetOpenAlarms();

    private static string Fmt(double v) => v.ToString("0.0", CultureInfo.GetCultureInfo("nb-NO"));
}
