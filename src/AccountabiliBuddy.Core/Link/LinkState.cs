namespace AccountabiliBuddy.Core.Link;

/// <summary>
/// The PC's session as the device shows it, and what it can start. Times are absolute, so the device counts down by
/// itself between updates.
/// </summary>
/// <param name="Mode">"manual", "pomodoro" or "scheduled"; null when no session runs.</param>
/// <param name="Phase">"focus", "shortBreak", "longBreak" or "idle".</param>
/// <param name="CountsAsFocus">False for scheduled blocks (they block but aren't focus) and breaks.</param>
/// <param name="PhaseEndsAt">Null for a session with no set end (count-up).</param>
/// <param name="CountUpProfileId">The profile "Count up" starts; null = "Quick Start", made on first use.</param>
/// <param name="Tasks">The running session's guided checklist (ADR 24); null for none (left out of the JSON).</param>
/// <param name="CurrentTask">The task or subtask picked to show on the bar; null for none (the first open task shows).</param>
public sealed record LinkState(
    string? Mode,
    string Phase,
    bool CountsAsFocus,
    Guid? ProfileId,
    string? ProfileName,
    DateTimeOffset? PhaseStartedAt,
    DateTimeOffset? PhaseEndsAt,
    IReadOnlyList<LinkProfile> Profiles,
    Guid? CountUpProfileId,
    DateTimeOffset SentAt,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<AccountabiliBuddy.Core.Domain.SessionTask>? Tasks = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    Guid? CurrentTask = null)
{
    public bool IsActive => Mode is not null;
}
