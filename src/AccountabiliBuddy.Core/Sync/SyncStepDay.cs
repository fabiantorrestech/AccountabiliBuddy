namespace AccountabiliBuddy.Core.Sync;

/// <summary>One day's step count; <paramref name="Date"/> is the day on the phone's calendar.</summary>
public sealed record SyncStepDay(DateOnly Date, int Count);
