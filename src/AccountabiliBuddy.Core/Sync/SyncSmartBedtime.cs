namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// A phone's smart bedtime (ADR 34): per kind of night, what it learned. Only derived numbers: never sleep sessions,
/// times of phone use or app names. Null for a kind that's off.
/// </summary>
public sealed record SyncSmartBedtime(SyncSmartLevel? Workdays, SyncSmartLevel? OffDays);
