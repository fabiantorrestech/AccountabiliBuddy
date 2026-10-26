namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// A phone's estimated wake time per kind of morning (ADR 40): the median of its wake-up app's past alarms, else of
/// measured wake times. Only the two times ("HH:mm", invariant): never alarm lists, sleep sessions or app names. Null for
/// a kind without enough data.
/// </summary>
public sealed record SyncWakeEstimate(string? Workdays, string? OffDays);
