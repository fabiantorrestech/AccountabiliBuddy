namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// A PC's running session as its sync file carries it (ADR 26), so a phone with Drive and no link still counts the
/// phase down. Only sessions that count as focus (not old scheduled blocks); null when none runs.
/// </summary>
/// <param name="Profile">The profile's name.</param>
/// <param name="PhaseStartedAt">When the current focus or break began (moved on by time stepped away).</param>
/// <param name="PhaseEndsAt">When it ends; null for a session with no set end.</param>
/// <param name="IsBreak">A break between focus phases (Pomodoro).</param>
/// <param name="AwaySince">Set while the user has stepped away (the countdown is paused).</param>
public sealed record SyncRunning(string? Profile, DateTimeOffset PhaseStartedAt, DateTimeOffset? PhaseEndsAt, bool IsBreak, DateTimeOffset? AwaySince);
