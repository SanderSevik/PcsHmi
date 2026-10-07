using PcsHmi.Models;

namespace PcsHmi.Services;

/// <summary>Siste kjente tilstand. HMI-sidene abonnerer på Changed.</summary>
public class PlantState
{
    public Measurement? Latest { get; private set; }
    public bool Connected { get; private set; }
    public event Action? Changed;

    public void Update(Measurement m)
    {
        Latest = m;
        Connected = true;
        Changed?.Invoke();
    }

    public void SetConnected(bool connected)
    {
        if (Connected == connected) return;
        Connected = connected;
        Changed?.Invoke();
    }

    public void Notify() => Changed?.Invoke();
}
