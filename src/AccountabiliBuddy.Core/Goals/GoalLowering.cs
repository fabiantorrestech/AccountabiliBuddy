namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// Changes and the looser-change rule for goals (brief §5.7, §5.10):
/// <list type="bullet">
/// <item>Raising a target, or adding one, applies at once (as do adding a goal, a source or a reminder).</item>
/// <item>Lowering the built-in Deep work goal's daily or weekly target, or removing it ("Track time only" removes both), waits
/// the looser-change delay (<see cref="Domain.LockSettings.LooseningDelay"/>) and never applies during a session; on top of
/// that the PC's existing rule for the daily goal still holds: no lowering while the work-hours guard has work left today
/// (<c>GoalCardViewModel.LowerWaits</c>, passed in as <c>lowerWaitsNow</c>).</item>
/// <item>In ProductaBuddy mode lowering Deep work skips the delay and the session rule (brief §5.10) but keeps the existing
/// work-hours rule, which already applies in both modes today (never loosened here; Q362).</item>
/// <item>Every other goal feeds no guard, so lowering it applies at once in both modes (brief §10 Q6).</item>
/// </list>
/// Goal counts never feed a guard, the goal unlock or <c>focusDay</c>, which keep reading counted focus only.
/// </summary>
public static class GoalLowering
{
    /// <summary>What happens when <paramref name="target"/>'s amount is changed to <paramref name="newAmount"/> (null = no target).</summary>
    /// <param name="productaBuddy">The app is in ProductaBuddy mode (ADR 68).</param>
    /// <param name="lowerWaitsNow">The existing daily-goal rule: the work-hours guard has work left today.</param>
    /// <param name="looserDelay">The looser-change delay (10 minutes by default).</param>
    public static GoalChangeDecision Decide(
        Goal goal, GoalTarget target, double? newAmount, bool productaBuddy, bool lowerWaitsNow, DateTimeOffset now, TimeSpan looserDelay)
    {
        if (Nullable.Equals(target.Amount, newAmount))
        {
            return new GoalChangeDecision(GoalChangeOutcome.Unchanged);
        }

        if (!IsLooser(target.Direction, target.Amount, newAmount) || !goal.BuiltIn)
        {
            return new GoalChangeDecision(GoalChangeOutcome.ApplyNow);
        }

        if (productaBuddy)
        {
            return lowerWaitsNow ? new GoalChangeDecision(GoalChangeOutcome.Wait, now) : new GoalChangeDecision(GoalChangeOutcome.ApplyNow);
        }

        return new GoalChangeDecision(GoalChangeOutcome.Wait, now + looserDelay);
    }

    /// <summary>
    /// Whether a waiting change may apply now: its time has come, no session runs (outside ProductaBuddy mode) and the existing
    /// work-hours rule doesn't hold it.
    /// </summary>
    public static bool CanApply(GoalPendingChange pending, DateTimeOffset now, bool sessionRunning, bool productaBuddy, bool lowerWaitsNow)
    {
        return now >= pending.AppliesAt && (productaBuddy || !sessionRunning) && !lowerWaitsNow;
    }

    /// <summary>
    /// Whether going from <paramref name="from"/> to <paramref name="to"/> is looser: removing a target, a lower at-least amount
    /// or a higher at-most cap. Adding a target is stricter; moving a level to reach is neither (it applies at once).
    /// </summary>
    public static bool IsLooser(GoalDirection direction, double? from, double? to)
    {
        if (Nullable.Equals(from, to) || from is not double before)
        {
            return false;
        }

        if (to is not double after)
        {
            return true;
        }

        return direction switch
        {
            GoalDirection.AtLeast => after < before,
            GoalDirection.AtMost => after > before,
            _ => false,
        };
    }

    /// <summary>Pausing and deleting apply at once for every goal but Deep work, which can only switch to "Track time only".</summary>
    public static bool CanPauseOrDelete(Goal goal)
    {
        return !goal.BuiltIn;
    }
}
