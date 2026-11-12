namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// The Target tile's "What it takes" pop-up and the New goal box (brief §5.6, change note §4a):
/// <list type="bullet">
/// <item>Remaining = target − current; days left = the target date − today, both included.</item>
/// <item>Each day = remaining ÷ days left; each week = each day × 7.</item>
/// <item>Pace = the average over the trailing weeks (4 by default; the days before today, paused days left out); projected
/// finish = today + remaining ÷ pace, in whole days.</item>
/// <item>Verdict: met; green when the goal's own period target, as a daily rate, covers each day; else amber with how much more
/// per day and per week; "The date has passed"; or no date (remaining, pace and projection only).</item>
/// </list>
/// Minutes are shown rounded up to friendly values (<see cref="FriendlyMinutes"/>, <see cref="MinutesText"/>): whole minutes
/// under an hour, then 5-minute steps, so doing the amount shown is always enough.
/// </summary>
/// <param name="Remaining">What is left, in the goal's unit (0 once met).</param>
/// <param name="DaysLeft">Days left to the date, today included; null without a date or once it has passed.</param>
/// <param name="OwnPerDay">The goal's own period target as a daily rate; null without one (a level to reach has none).</param>
/// <param name="MorePerDay">Amber: how much more than <paramref name="OwnPerDay"/> each day needs; null otherwise.</param>
public sealed record GoalWhatItTakes(
    GoalWhatItTakesVerdict Verdict,
    double Remaining,
    int? DaysLeft,
    double? EachDay,
    double? EachWeek,
    double? PacePerDay,
    DateOnly? ProjectedFinish,
    double? OwnPerDay,
    double? MorePerDay,
    double? MorePerWeek)
{
    public const int DefaultPaceWeeks = 4;

    /// <summary>For a goal with a long-term amount (not an at-most one); null otherwise.</summary>
    public static GoalWhatItTakes? For(Goal goal, GoalSourceData data, DateOnly today, int paceWeeks = DefaultPaceWeeks)
    {
        if (GoalStatus.Target(goal, data, today) is not GoalTargetProgress progress || goal.Target.Direction == GoalDirection.AtMost)
        {
            return null;
        }

        bool down = goal.Target.Direction == GoalDirection.Reach && GoalCounting.ReachesDown(goal, data);
        double remaining = goal.Target.Direction == GoalDirection.Reach
            ? (down ? progress.Current - progress.Target : progress.Target - progress.Current)
            : progress.Target - progress.Current;
        return Compute(remaining, today, goal.Target.TargetDate, Pace(goal, data, today, paceWeeks, down), OwnRate(goal, today));
    }

    /// <summary>The maths on its own (the New goal box, before the goal exists).</summary>
    public static GoalWhatItTakes Compute(double remaining, DateOnly today, DateOnly? targetDate, double? pacePerDay, double? ownPerDay)
    {
        double left = Math.Max(0, remaining);
        DateOnly? projected = left > 0 && pacePerDay is double pace && pace > 0 ? today.AddDays((int)Math.Ceiling((left / pace) - 1e-9)) : null;
        if (left <= 0)
        {
            return new GoalWhatItTakes(GoalWhatItTakesVerdict.Met, 0, null, null, null, pacePerDay, null, ownPerDay, null, null);
        }

        if (targetDate is not DateOnly date)
        {
            return new GoalWhatItTakes(GoalWhatItTakesVerdict.NoDate, left, null, null, null, pacePerDay, projected, ownPerDay, null, null);
        }

        if (today > date)
        {
            return new GoalWhatItTakes(GoalWhatItTakesVerdict.DatePassed, left, null, null, null, pacePerDay, projected, ownPerDay, null, null);
        }

        int daysLeft = date.DayNumber - today.DayNumber + 1;
        double eachDay = left / daysLeft;
        double eachWeek = eachDay * 7;
        if (ownPerDay is double own && own >= eachDay)
        {
            return new GoalWhatItTakes(GoalWhatItTakesVerdict.Covered, left, daysLeft, eachDay, eachWeek, pacePerDay, projected, ownPerDay, null, null);
        }

        double more = eachDay - (ownPerDay ?? 0);
        return new GoalWhatItTakes(GoalWhatItTakesVerdict.NeedsMore, left, daysLeft, eachDay, eachWeek, pacePerDay, projected, ownPerDay, more, more * 7);
    }

    /// <summary>Minutes rounded up to a friendly value: whole minutes under an hour, then 5-minute steps (84.2 → 85).</summary>
    public static int FriendlyMinutes(double minutes)
    {
        if (minutes <= 0)
        {
            return 0;
        }

        int whole = (int)Math.Ceiling(minutes - 1e-9);
        return whole < 60 ? whole : (int)(Math.Ceiling((minutes - 1e-9) / 5) * 5);
    }

    /// <summary>"12 min", "1h", "1h 25m".</summary>
    public static string MinutesText(int minutes)
    {
        if (minutes < 60)
        {
            return $"{minutes} min";
        }

        int hours = minutes / 60;
        int rest = minutes % 60;
        return rest == 0 ? $"{hours}h" : $"{hours}h {rest}m";
    }

    /// <summary>The goal's own period target as a daily rate (at-least goals only); null without one.</summary>
    public static double? OwnRate(Goal goal, DateOnly today)
    {
        if (goal.Target.Direction != GoalDirection.AtLeast || goal.Target.Amount is not double amount)
        {
            return null;
        }

        int length = goal.Target.Period switch
        {
            GoalPeriod.Week => 7,
            GoalPeriod.TwoWeeks => 14,
            GoalPeriod.Month => DateTime.DaysInMonth(today.Year, today.Month),
            _ => 1,
        };
        double perPeriod = goal.Target.WeeklyDays is int days && goal.Target.Period != GoalPeriod.Day ? amount * days : amount;
        return perPeriod / length;
    }

    /// <summary>The trailing average a day over <paramref name="weeks"/> weeks before today (live days only); null with none.</summary>
    public static double? Pace(Goal goal, GoalSourceData data, DateOnly today, int weeks, bool reachesDown)
    {
        DateOnly from = today.AddDays(-7 * weeks);
        IReadOnlyDictionary<DateOnly, GoalDayState> days = GoalCounting.History(goal, data, today, from);
        if (goal.Target.Direction == GoalDirection.Reach)
        {
            List<GoalDayState> values = days.Values
                .Where(state => state.HasValue && goal.IsAliveOn(state.Day, data.Zone))
                .OrderBy(state => state.Day)
                .ToList();
            if (values.Count < 2 || values[^1].Day.DayNumber - values[0].Day.DayNumber < 1)
            {
                return null;
            }

            double moved = reachesDown ? values[0].Value - values[^1].Value : values[^1].Value - values[0].Value;
            return moved / (values[^1].Day.DayNumber - values[0].Day.DayNumber);
        }

        List<GoalDayState> live = days.Values.Where(state => state.Day < today && goal.IsAliveOn(state.Day, data.Zone)).ToList();
        return live.Count == 0 ? null : live.Sum(state => state.Value) / live.Count;
    }
}
