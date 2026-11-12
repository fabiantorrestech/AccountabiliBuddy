namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// One device usage row for <see cref="GoalAppMatching"/>: <paramref name="Id"/> is the phone's package or the PC's process name,
/// <paramref name="Label"/> the name the device shows (or null), <paramref name="Site"/> the domain of a PC site row (or null).
/// </summary>
public sealed record GoalAppRow(string Id, string? Label, string? Site, double Minutes);
