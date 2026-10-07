namespace PcsHmi.Models;

/// <summary>Én måling fra Arduinoen. Tidsstempel settes på Pi-en (UTC).</summary>
public record Measurement(
    DateTime Timestamp,
    double T1,
    int Ldr,
    double Setpoint,
    bool Heat,
    string Mode,   // "AUTO" eller "MAN"
    int Dim,       // 0-255
    int Light);    // 0-255, faktisk PWM ut på hvit LED

public record AlarmRecord(
    long Id,
    string Type,
    string Message,
    double? Value,
    DateTime Start,
    DateTime? End,
    bool Acknowledged)
{
    public bool Active => End is null;
}
