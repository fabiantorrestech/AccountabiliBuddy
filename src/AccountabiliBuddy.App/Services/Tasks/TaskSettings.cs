using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;

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
public sealed record TaskSettings(
    int SessionActions,
    bool NextStepTakesSlot,
    int? PendingSessionActions = null,
    DateTimeOffset? PendingFrom = null,
    StepAwayTimer StepAway = StepAwayTimer.KeepTimerRunning,
    StepAwayTimer? PendingStepAway = null,
    DateTimeOffset? PendingStepAwayFrom = null)
{
    public static TaskSettings Default { get; } = new(Core.Tasks.SessionActions.DefaultCap, true, StepAway: StepAwayTimerPolicy.Default);
}
