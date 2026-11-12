namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// The numbers "Adjust the target" pre-fills in Edit goal › Target (brief §5.8: "move the date or the amount"), from the review's own
/// facts (<see cref="GoalReviewSuggestion"/>), rule-based on the device:
/// <list type="number">
/// <item>A long-term target whose finish at this pace is past its date: the date moves to that finish.</item>
/// <item>"N of the period's days" above one day: one day fewer.</item>
/// <item>An at-least amount per period above what the trailing pace delivers: the pace's amount for the period (time rounded to 5
/// minutes, other amounts to a whole number, never below one step).</item>
/// </list>
/// Null when none applies (Edit goal › Target then opens as it is). Pre-filled only: nothing is saved until the user saves, and a
/// lowering of Deep work still waits (<see cref="GoalLowering"/>).
/// </summary>
/// <param name="TargetDate">The moved long-term date; null = unchanged.</param>
/// <param name="Amount">The new amount per period (Deep work: its daily minutes); null = unchanged.</param>
/// <param name="WeeklyDays">The new "N of the period's days"; null = unchanged.</param>
public sealed record GoalReviewAdjustment(DateOnly? TargetDate, double? Amount, int? WeeklyDays)
{
    public static GoalReviewAdjustment? For(Goal goal, GoalReviewSuggestion suggestion, DateOnly today)
    {
        GoalTarget target = goal.Target;
        if (target.TargetDate is DateOnly date && suggestion.ProjectedFinish is DateOnly finish && finish > date && finish > today)
        {
            return new GoalReviewAdjustment(finish, null, null);
        }

        if (target.WeeklyDays is int days && days > 1)
        {
            return new GoalReviewAdjustment(null, null, days - 1);
        }

        if (target.Amount is double amount && target.Direction == GoalDirection.AtLeast && target.WeeklyDays is null
            && suggestion.PacePerDay is double pace && pace > 0)
        {
            double step = target.Unit == GoalUnit.Minutes ? 5 : 1;
            double perPeriod = Math.Max(step, Math.Round(pace * DaysIn(target.Period) / step) * step);
            if (perPeriod < amount)
            {
                return new GoalReviewAdjustment(null, perPeriod, null);
            }
        }

        return null;
    }

    private static int DaysIn(GoalPeriod period) => period switch
    {
        GoalPeriod.Day => 1,
        GoalPeriod.Week => 7,
        GoalPeriod.TwoWeeks => 14,
        _ => 30,
    };
}
