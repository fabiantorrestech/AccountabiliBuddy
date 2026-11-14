using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Sync;
using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// This PC's Tasks settings (document "tasks.settings", device-local, kept per mode, ADR 69, 76, 77): AccountabiliBuddy's under that
/// key, ProductaBuddy's under the key plus <see cref="ModeSettings.CompanionSuffix"/>, with the same defaults. Settings › Tasks shows
/// and edits the active mode's copy (<c>TaskSettingsViewModel</c>).
/// </summary>
/// <param name="SessionActions">How many actions a session holds: <see cref="Core.Tasks.SessionActions.MinCap"/> to <see cref="Core.Tasks.SessionActions.MaxCap"/>, 3 by default.</param>
/// <param name="NextStepTakesSlot">When a focused step is finished, the next open step of its task takes its place (on by default).</param>
/// <param name="PendingSessionActions">A larger number of actions waiting for the looser-change wait to end; null when none waits.</param>
/// <param name="PendingFrom">When <paramref name="PendingSessionActions"/> takes effect (trusted clock), unless a session runs then.</param>
/// <param name="StepAway">"When I step away" (ADR 77): keep the timer running (default) or pause it.</param>
/// <param name="PendingStepAway">Pausing the timer, waiting for the looser-change wait to end; null when nothing waits.</param>
/// <param name="PendingStepAwayFrom">When <paramref name="PendingStepAway"/> takes effect, unless a session runs then.</param>
/// <param name="UntouchedWeeks">A task counts as untouched after this many weeks (<see cref="TaskUntouched.MinWeeks"/> to <see cref="TaskUntouched.MaxWeeks"/>, 3 by default).</param>
/// <param name="TidyUpOn">The weekly tidy-up card shows (on by default).</param>
/// <param name="TidyUpDay">The day it shows from (Monday by default).</param>
/// <param name="PinHighlights">New views pin highlighted tasks to the top (on by default; each view can change it).</param>
/// <param name="DefaultViewId">
/// Before the setting became the account's: the view the Shelf opened with on this PC. Read once to take it over
/// (<see cref="TaskAccountSettingsService.AdoptLegacyAsync"/>), then cleared; never written again.
/// </param>
/// <param name="CustomMarkers">Project markers the user taught (device-local, see HANDOFF-QUESTIONS); null for none.</param>
/// <param name="SideTask">"When I start a side task" (ADR 77): keep the timer running (default) or pause it.</param>
/// <param name="PendingSideTask">Pausing the timer for side tasks, waiting for the looser-change wait to end; null when nothing waits.</param>
/// <param name="PendingSideTaskFrom">When <paramref name="PendingSideTask"/> takes effect, unless a session runs then.</param>
/// <param name="DeclinedMarkers">Marker styles the add box offered to learn and the user said No to: never offered again; null for none.</param>
/// <param name="CompactInSession">Tasks switches to Compact rows while a session runs (on by default); the page's own Normal / Compact stays as it was.</param>
/// <param name="HideShelfInSession">The Shelf hides while a session runs, its rail reading "hidden during session" (on by default).</param>
/// <param name="SessionView">How This session lists a running session's actions: Quiet (default) or Grouped.</param>
/// <param name="PendingCompactInSession">Compact rows turned off (looser), waiting for the looser-change wait to end; null when nothing waits.</param>
/// <param name="PendingCompactFrom">When <paramref name="PendingCompactInSession"/> takes effect, unless a session runs then.</param>
/// <param name="PendingHideShelfInSession">The Shelf shown during a session (looser), waiting; null when nothing waits.</param>
/// <param name="PendingHideShelfFrom">When <paramref name="PendingHideShelfInSession"/> takes effect, unless a session runs then.</param>
/// <param name="PendingSessionView">Grouped (looser than Quiet), waiting; null when nothing waits.</param>
/// <param name="PendingSessionViewFrom">When <paramref name="PendingSessionView"/> takes effect, unless a session runs then.</param>
/// <param name="LabelStyle">How rows show their label: Dot (default), Pill before the title, Pill in the detail line. Display only.</param>
/// <param name="DefaultTimeLimitMinutes">The time limit a task typed in the add box starts with; 0 for none (25 by default, spec 02).</param>
/// <param name="SuggestShorterTitles">The add box's title help: the counter from 50 characters and "Move … to notes" (on by default).</param>
/// <param name="ShowAgeOnRows">Rows say "untouched N wk" once a task counts as untouched (on by default).</param>
/// <param name="AllowLunchDiscard">"Allow discarding a lunch" (ADR 88): the lunch card offers Discard it (on by default; per mode, this PC only).</param>
/// <param name="PendingAllowLunchDiscard">Discarding turned on (looser), waiting for the looser-change wait to end; null when nothing waits.</param>
/// <param name="PendingAllowLunchDiscardFrom">When <paramref name="PendingAllowLunchDiscard"/> takes effect, unless a session runs then.</param>
public sealed record TaskSettings(
    int SessionActions,
    bool NextStepTakesSlot,
    int? PendingSessionActions = null,
    DateTimeOffset? PendingFrom = null,
    StepAwayTimer StepAway = StepAwayTimer.KeepTimerRunning,
    StepAwayTimer? PendingStepAway = null,
    DateTimeOffset? PendingStepAwayFrom = null,
    int UntouchedWeeks = TaskUntouched.DefaultWeeks,
    bool TidyUpOn = true,
    DayOfWeek TidyUpDay = DayOfWeek.Monday,
    bool PinHighlights = true,
    Guid? DefaultViewId = null,
    IReadOnlyList<ProjectMarkerPattern>? CustomMarkers = null,
    StepAwayTimer SideTask = StepAwayTimer.KeepTimerRunning,
    StepAwayTimer? PendingSideTask = null,
    DateTimeOffset? PendingSideTaskFrom = null,
    IReadOnlyList<ProjectMarkerPattern>? DeclinedMarkers = null,
    bool CompactInSession = true,
    bool HideShelfInSession = true,
    SessionTaskView SessionView = SessionTaskView.Quiet,
    bool? PendingCompactInSession = null,
    DateTimeOffset? PendingCompactFrom = null,
    bool? PendingHideShelfInSession = null,
    DateTimeOffset? PendingHideShelfFrom = null,
    SessionTaskView? PendingSessionView = null,
    DateTimeOffset? PendingSessionViewFrom = null,
    TaskLabelStyle LabelStyle = TaskLabelStyle.Dot,
    int DefaultTimeLimitMinutes = TaskSettings.StandardTimeLimitMinutes,
    bool SuggestShorterTitles = true,
    bool ShowAgeOnRows = true,
    bool AllowLunchDiscard = true,
    bool? PendingAllowLunchDiscard = null,
    DateTimeOffset? PendingAllowLunchDiscardFrom = null)
{
    /// <summary>Default time limit's default (spec 02: "default from Settings (25)"; Pomodoro's focus length is 25 too).</summary>
    public const int StandardTimeLimitMinutes = 25;

    public static TaskSettings Default { get; } = new(Core.Tasks.SessionActions.DefaultCap, true, StepAway: StepAwayTimerPolicy.Default, SideTask: StepAwayTimerPolicy.Default);

    /// <summary>The lunch discard setting as the Core rule sees it.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public LunchDiscardSetting LunchDiscard => new(AllowLunchDiscard, PendingAllowLunchDiscard, PendingAllowLunchDiscardFrom);

    /// <summary>The time limit a new task from the add box gets; null for none.</summary>
    public int? NewTaskTimeLimit => DefaultTimeLimitMinutes > 0 ? DefaultTimeLimitMinutes : null;
}
