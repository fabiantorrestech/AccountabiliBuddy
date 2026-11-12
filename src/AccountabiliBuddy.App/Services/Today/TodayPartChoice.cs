namespace AccountabiliBuddy.App.Services.Today;

/// <summary>One part of a stored Today layout: its key (<see cref="TodayPartInfo.Key"/>) and where it sits.</summary>
public sealed record TodayPartChoice(string Key, TodayPartPlacement Placement);
