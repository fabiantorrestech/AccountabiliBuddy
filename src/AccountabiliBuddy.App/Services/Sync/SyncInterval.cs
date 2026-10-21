namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>How often this PC writes its sync file to the sync folder (ADR 22). It always writes when a session ends.</summary>
public enum SyncInterval
{
    /// <summary>On every change of its own, 5 seconds after the last one.</summary>
    Instant,
    OneMinute,
    FiveMinutes,
    FifteenMinutes,
}
