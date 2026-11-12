namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// The heatmap's intensity (brief §5.5): 4 steps of the goal's colour ramp. 0 = nothing (also paused, not due), 1 = under the
/// day's target, 2 = at it, 3 = well over (at least <see cref="WellOver"/> times it). The day's target is the daily amount (or
/// the amount for each of "N days"), else the period's amount spread over its days; a habit's is its threshold (a hand tick is
/// "at"). An at-most day under its cap is "at", over it "under". Missed and under-target cells are also drawn as §3.8 says.
/// </summary>
public static class GoalHeat
{
    public const double WellOver = 1.5;

    public static int Step(Goal goal, GoalDayState state)
    {
        if (state.Status is GoalDayStatus.Paused || !state.HasValue)
        {
            return 0;
        }

        if (goal.Kind == GoalKind.Habit)
        {
            double? threshold = goal.Sources
                .Where(rule => rule.Automatic && rule.CountsTime && rule.ThresholdMinutes is > 0)
                .Select(rule => rule.ThresholdMinutes)
                .Min();
            if (!state.Hit && state.Value < 1)
            {
                return state.Minutes > 0 ? 1 : 0;
            }

            return threshold is double minutes && state.Minutes >= minutes * WellOver ? 3 : 2;
        }

        if (goal.Target.Direction == GoalDirection.AtMost)
        {
            return goal.Target.Amount is double cap && state.Value > cap ? 1 : 2;
        }

        if (goal.Target.Direction == GoalDirection.Reach)
        {
            return state.Hit ? 2 : 1;
        }

        double? dayTarget = DayTarget(goal, state.Day);
        if (state.Value <= 0)
        {
            return 0;
        }

        if (dayTarget is not double target || target <= 0)
        {
            return 2;
        }

        double ratio = state.Value / target;
        return ratio < 1 ? 1 : ratio < WellOver ? 2 : 3;
    }

    private static double? DayTarget(Goal goal, DateOnly day)
    {
        if (goal.Target.Amount is not double amount)
        {
            return null;
        }

        if (goal.Target.CountsDays)
        {
            return amount;
        }

        int length = goal.Target.Period switch
        {
            GoalPeriod.Week => 7,
            GoalPeriod.TwoWeeks => 14,
            GoalPeriod.Month => DateTime.DaysInMonth(day.Year, day.Month),
            _ => 1,
        };
        return amount / length;
    }
}
