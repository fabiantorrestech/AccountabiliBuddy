namespace AccountabiliBuddy.App.Services;

/// <summary>
/// A line kept during a session (redesign phase 5): shown on that session's review card and in History, and in the Archive's Notes.
/// <paramref name="By"/> is the device that wrote it (empty for a note stored before ADR 87 and not yet stamped): a note from another
/// device (ADR 87) is marked "from &lt;device&gt;" in the Archive, and never shows in this device's own session review.
/// </summary>
public sealed record SessionNote(Guid Id, string Text, DateTimeOffset At, Guid By = default);
