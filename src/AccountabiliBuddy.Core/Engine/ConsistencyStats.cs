namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Streaks and rates over days of deep work. Only workdays count: an off day never counts against the user, and today
/// doesn't until its goal is met (it isn't over yet).
/// </summary>
public static class ConsistencyStats
{
    /// <summary>The heat levels a day can have, 0 (nothing) to <see cref="MaxLevel"/>.</summary>
    public const int MaxLevel = 4;

    /// <param name="days">One entry per date, oldest first (<see cref="DeepWork.DaysIn"/>).</param>
    /// <param name="isWorkday">Whether a date is a workday in the day plan.</param>
    public static ConsistencySummary Summarize(IReadOnlyList<DeepWorkDay> days, TimeSpan goal, Func<DateOnly, bool> isWorkday, DateOnly today)
    {
        var counted = new List<(DeepWorkDay Day, bool Met)>();
        bool hasOffDays = false;
        foreach (DeepWorkDay day in days)
        {
            if (!isWorkday(day.Date))
            {
                hasOffDays = true;
                continue;
            }

            bool met = day.Focused >= goal;
            if (day.Date > today || (day.Date == today && !met))
            {
                continue;
            }

            counted.Add((day, met));
        }

        // The run left after the loop is the streak that ends at the latest counted workday.
        int best = 0;
        int run = 0;
        foreach ((DeepWorkDay _, bool met) in counted)
        {
            run = met ? run + 1 : 0;
            best = Math.Max(best, run);
        }

        var perWeekday = counted
            .GroupBy(entry => entry.Day.Date.DayOfWeek)
            .Select(group => (Day: group.Key, Counted: group.Count(), Met: group.Count(entry => entry.Met)))
            .Where(group => group.Counted >= 2)
            .OrderByDescending(group => (double)group.Met / group.Counted)
            .ThenByDescending(group => group.Counted)
            .ToList();
        bool hasReliable = perWeekday.Count > 0;

        return new ConsistencySummary(
            run,
            best,
            counted.Count,
            counted.Count(entry => entry.Met),
            hasReliable ? perWeekday[0].Day : null,
            hasReliable ? (int)Math.Round(100.0 * perWeekday[0].Met / perWeekday[0].Counted) : 0,
            hasOffDays);
    }

    /// <summary>0 for no focus, 1 under half the goal, 2 under the goal, 3 for the goal met, 4 for half as much again.</summary>
    public static int Level(TimeSpan focused, TimeSpan goal)
    {
        if (focused <= TimeSpan.Zero)
        {
            return 0;
        }

        double share = focused / goal;
        return share < 0.5 ? 1 : share < 1 ? 2 : share < 1.5 ? 3 : MaxLevel;
    }
}
