namespace AccountabiliBuddy.Core.Ports;

/// <summary>A battery's charge, 0 to 100, and whether it is charging.</summary>
public sealed record BatteryLevel(int Percent, bool IsCharging);
