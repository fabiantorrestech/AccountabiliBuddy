namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// A stretch the user was using a PC (keyboard or mouse, gaps under a minute joined), as its sync file carries today's
/// (ADR 26, times only): another PC tells from it that the user just came over from this one (design K).
/// </summary>
public sealed record SyncInputRun(DateTimeOffset Start, DateTimeOffset End);
