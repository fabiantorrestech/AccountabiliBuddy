namespace AccountabiliBuddy.Core.Sync;

/// <summary>Today's flexible lunch as a device started it (schema v8): every device takes the newest one for the day.</summary>
public sealed record SyncLunch(DateTimeOffset Start, DateTimeOffset End);
