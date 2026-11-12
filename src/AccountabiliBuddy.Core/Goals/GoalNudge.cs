namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// "Nudge when the week falls behind" (brief §5.9, §5.10): one nudge the morning after the goal drops below pace. It dropped when
/// this period's status (<see cref="GoalStatus.ThisPeriod"/>) is Behind today but wasn't as of yesterday; a goal that stays behind
/// isn't nudged again until it catches up and drops once more. A goal paused (or not yet made) yesterday or today isn't nudged.
/// Suggestions only: nothing locks or waits.
/// </summary>
public static class GoalNudge
{
    /// <returns>Whether <paramref name="goal"/> dropped below pace with yesterday, so today's morning nudge is due.</returns>
    public static bool DroppedBehind(Goal goal, GoalSourceData data, DateOnly today, GoalWeekStart weekStart)
    {
        DateOnly yesterday = today.AddDays(-1);
        if (!goal.IsAliveOn(today, data.Zone) || !goal.IsAliveOn(yesterday, data.Zone))
        {
            return false;
        }

        return GoalStatus.ThisPeriod(goal, data, today, weekStart) == GoalPeriodStatus.Behind
            && GoalStatus.ThisPeriod(goal, data, yesterday, weekStart) != GoalPeriodStatus.Behind;
    }
}
