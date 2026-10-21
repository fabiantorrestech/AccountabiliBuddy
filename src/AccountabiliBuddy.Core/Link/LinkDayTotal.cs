namespace AccountabiliBuddy.Core.Link;

/// <summary>One day's focused minutes against its planned work.</summary>
public sealed record LinkDayTotal(DateOnly Date, int FocusedMinutes, int PlannedMinutes);
