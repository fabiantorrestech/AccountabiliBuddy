namespace AccountabiliBuddy.Core.Ports;

/// <summary>An audio output's master state: which output, its level (0 to 1) and whether it is muted.</summary>
public sealed record MasterVolumeState(string DeviceId, float Level, bool Muted);
