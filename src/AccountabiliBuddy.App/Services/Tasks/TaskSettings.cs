using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// This PC's Tasks settings (document "tasks.settings", device-local, kept per mode, ADR 69, 76, 77): AccountabiliBuddy's under that
/// key, ProductaBuddy's under the key plus <see cref="ModeSettings.CompanionSuffix"/>, with the same defaults. Settings › Tasks shows
/// them (Phase 7); until then only the defaults apply.
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
    DateTimeOffset? PendingSideTaskFrom = null)
{
    public static TaskSettings Default { get; } = new(Core.Tasks.SessionActions.DefaultCap, true, StepAway: StepAwayTimerPolicy.Default, SideTask: StepAwayTimerPolicy.Default);
}
