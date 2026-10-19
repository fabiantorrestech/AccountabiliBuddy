namespace AccountabiliBuddy.Core.Ports;

/// <summary>Identifies the current boot of the computer, so saved elapsed-time counters are only compared within one boot.</summary>
public interface IBootIdProvider
{
    /// <returns>An id that changes with every boot, or null if the platform can't tell.</returns>
    string? GetBootId();
}
