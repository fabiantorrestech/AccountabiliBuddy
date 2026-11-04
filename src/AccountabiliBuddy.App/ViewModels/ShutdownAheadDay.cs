namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One day with calendar events on the routine's look-ahead step.</summary>
/// <param name="Heading">E.g. "Tomorrow · Tue, Nov 3".</param>
/// <param name="Lines">The day's events, each with its time ("9:00 AM Standup", "All day Holiday").</param>
public sealed record ShutdownAheadDay(string Heading, IReadOnlyList<string> Lines);
