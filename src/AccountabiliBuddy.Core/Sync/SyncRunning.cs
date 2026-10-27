using System.Text.Json.Serialization;

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
/// <param name="CurrentTask">ADR 42: the task or subtask picked to show on the bar (its id, as the link's state); null for none.</param>
/// <param name="ProfileId">ADR 42: the running profile's id.</param>
/// <param name="CountUpProfileId">ADR 42: the profile "Quick Start with Default" starts; null when it isn't made yet.</param>
/// <param name="PlanningStartedAt">Phase 11: set while the session is in its planning stage (not focus time); when it began. While planning,
/// <see cref="PhaseStartedAt"/> and <see cref="PhaseEndsAt"/> are the focus phase's projected start and end (planning's end and its full
/// length after it), so a build that doesn't know planning still shows a sensible countdown. Older builds leave it out.</param>
/// <param name="PlanningEndsAt">Phase 11: when planning ends (time up); it keeps counting past it until the user answers "Ready?".</param>
public sealed record SyncRunning(
    string? Profile,
    DateTimeOffset PhaseStartedAt,
    DateTimeOffset? PhaseEndsAt,
    bool IsBreak,
    DateTimeOffset? AwaySince,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? CurrentTask = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ProfileId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? CountUpProfileId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? PlanningStartedAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? PlanningEndsAt = null);
