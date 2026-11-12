namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// The streak (brief §5.5): consecutive met periods up to yesterday's (a daily goal's days, else its weeks, two weeks or months),
/// the current one added once it is met. A period with nothing asked of it (paused through, or before the goal existed in
/// part) is skipped, never a break; the first unmet period ends it. The 14-day strip (<see cref="GoalStrip14"/>) shows the
/// same days.
/// </summary>
public static class GoalStreak
{
    public static int Count(Goal goal, GoalSourceData data, DateOnly today, GoalWeekStart weekStart)
    {
        DateOnly created = goal.CreatedOn(data.Zone);
        if (today < created)
        {
            return 0;
        }

        IReadOnlyDictionary<DateOnly, GoalDayState> days = GoalCounting.History(goal, data, today);
        bool reachesDown = GoalCounting.ReachesDown(goal, data);
        GoalPeriodSpan span = GoalPeriods.PeriodOf(goal.Target.Period, today, weekStart, created);
        // An at-most period isn't met until it is over (being under the cap so far proves nothing yet).
        bool currentCanCount = GoalCounting.CountsDays(goal) || goal.Target.Direction != GoalDirection.AtMost;
        int streak = currentCanCount && GoalStatus.Evaluate(goal, data, span, days, today, reachesDown).Met ? 1 : 0;

        for (span = GoalPeriods.Previous(goal.Target.Period, span, weekStart, created); span.End >= created; span = GoalPeriods.Previous(goal.Target.Period, span, weekStart, created))
        {
            GoalPeriodResult result = GoalStatus.Evaluate(goal, data, span, days, today, reachesDown);
            if (result.Skipped)
            {
                continue;
            }

            if (!result.Met)
            {
                break;
            }

            streak++;
        }

        return streak;
    }
}
