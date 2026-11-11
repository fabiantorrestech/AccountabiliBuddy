using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// This PC's Tasks settings (document "tasks.settings", device-local, ADR 76). Settings › Tasks shows them (Phase 7); until then only
/// the defaults apply.
/// </summary>
/// <param name="SessionActions">How many actions a session holds: <see cref="SessionActions.MinCap"/> to <see cref="SessionActions.MaxCap"/>, 3 by default.</param>
/// <param name="NextStepTakesSlot">When a focused step is finished, the next open step of its task takes its place (on by default).</param>
/// <param name="PendingSessionActions">A larger number of actions waiting for the looser-change wait to end; null when none waits.</param>
/// <param name="PendingFrom">When <paramref name="PendingSessionActions"/> takes effect (trusted clock), unless a session runs then.</param>
public sealed record TaskSettings(
    int SessionActions,
    bool NextStepTakesSlot,
    int? PendingSessionActions = null,
    DateTimeOffset? PendingFrom = null)
{
    public static TaskSettings Default { get; } = new(Core.Tasks.SessionActions.DefaultCap, true);
}
