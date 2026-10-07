namespace PcsHmi.Services;

/// <summary>Innstillinger fra "Pcs"-seksjonen i appsettings.json.</summary>
public class PcsOptions
{
    public string PortName { get; set; } = "/dev/ttyACM0";
    public int BaudRate { get; set; } = 9600;
    public string DatabasePath { get; set; } = "pcs.db";
    public int LogIntervalSeconds { get; set; } = 5;
    public double HighTempLimitC { get; set; } = 28.0;
    public double LowTempLimitC { get; set; } = 15.0;
    public double AlarmHysteresisC { get; set; } = 0.5;
    public int CommTimeoutSeconds { get; set; } = 5;
}
