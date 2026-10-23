namespace AccountabiliBuddy.App.Services;

/// <summary>A line kept during a session (redesign phase 5): shown on that session's review card and in History.</summary>
public sealed record SessionNote(Guid Id, string Text, DateTimeOffset At);
