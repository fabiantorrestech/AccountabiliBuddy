namespace AccountabiliBuddy.Core.Ports;

/// <summary>This computer's own battery, read locally. Other devices' levels are not read here.</summary>
public interface IBatteryReader
{
    /// <summary>The charge now; null on a computer with no battery.</summary>
    BatteryLevel? Current { get; }

    /// <summary>Raised when the charge or the charging state changed. May be raised on a background thread.</summary>
    event EventHandler? Changed;
}
