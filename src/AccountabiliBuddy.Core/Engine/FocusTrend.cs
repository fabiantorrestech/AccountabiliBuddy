namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Deep work and drift as a daily average per week over the last weeks (the Stats page's Focus graph). Off days never pull
/// an average down, and a week that hasn't started counting yet is a gap, not a zero.
/// </summary>
public sealed class FocusTrend
{
    private const int EdgeWeeks = 3;

    private FocusTrend(IReadOnlyList<TrendWeek> weeks)
    {
        Weeks = weeks;
        List<TimeSpan> focus = weeks.Where(week => week.DailyFocus is not null).Select(week => week.DailyFocus!.Value).ToList();
        HasData = focus.Any(value => value > TimeSpan.Zero);
        TrendCount = focus.Count;
        if (focus.Count >= EdgeWeeks * 2)
        {
            Change = Average(focus.TakeLast(EdgeWeeks)) - Average(focus.Take(EdgeWeeks));
        }
    }

    public IReadOnlyList<TrendWeek> Weeks { get; }

    /// <summary>At least one week has deep work in it.</summary>
    public bool HasData { get; }

    /// <summary>How many weeks have a value.</summary>
    public int TrendCount { get; }

    /// <summary>The last weeks' daily average less the first weeks'; null until there are six weeks to compare.</summary>
    public TimeSpan? Change { get; }

    /// <summary>The newest week with a value.</summary>
    public TrendWeek? Latest => Weeks.LastOrDefault(week => week.DailyFocus is not null);

    /// <summary>The week before <see cref="Latest"/>, when it has a value.</summary>
    public TrendWeek? Previous
    {
        get
        {
            int index = Latest is { } latest ? Weeks.ToList().IndexOf(latest) - 1 : -1;
            return index >= 0 && Weeks[index].DailyFocus is not null ? Weeks[index] : null;
        }
    }

    /// <summary>The average daily drift over every week with a value; null with none.</summary>
    public TimeSpan? DriftAverage
    {
        get
        {
            List<TimeSpan> drift = Weeks.Where(week => week.DailyDrift is not null).Select(week => week.DailyDrift!.Value).ToList();
            return drift.Count == 0 ? null : Average(drift);
        }
    }

    /// <param name="days">One entry per date from the first with history up to today, oldest first.</param>
    /// <param name="driftPerDay">Drift time per date; a date missing from it has none.</param>
    /// <param name="isWorkday">Whether a date is a workday in the day plan.</param>
    /// <param name="goal">The daily goal: today, a workday not yet at it, isn't over and isn't counted (as in <see cref="ConsistencyStats"/>).</param>
    /// <param name="weeks">How many weeks to cover, the current one last.</param>
    public static FocusTrend Build(
        IReadOnlyList<DeepWorkDay> days,
        IReadOnlyDictionary<DateOnly, TimeSpan> driftPerDay,
        Func<DateOnly, bool> isWorkday,
        DateOnly today,
        TimeSpan goal,
        int weeks)
    {
        DateOnly thisMonday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        DateOnly firstDay = days.Count == 0 ? today : days[0].Date;
        var result = new List<TrendWeek>();
        for (int i = weeks - 1; i >= 0; i--)
        {
            DateOnly monday = thisMonday.AddDays(-7 * i);
            List<DeepWorkDay> counted = days
                .Where(day => day.Date >= monday && day.Date < monday.AddDays(7) && day.Date <= today && day.Date >= firstDay)
                .Where(day => isWorkday(day.Date) || day.Focused > TimeSpan.Zero)
                .Where(day => !(day.Date == today && isWorkday(day.Date) && day.Focused < goal))
                .ToList();
            if (counted.Count == 0)
            {
                result.Add(new TrendWeek(monday, null, null));
                continue;
            }

            TimeSpan focus = counted.Aggregate(TimeSpan.Zero, (sum, day) => sum + day.Focused);
            TimeSpan drift = counted.Aggregate(TimeSpan.Zero, (sum, day) => sum + (driftPerDay.TryGetValue(day.Date, out TimeSpan value) ? value : TimeSpan.Zero));
            result.Add(new TrendWeek(monday, focus / counted.Count, drift / counted.Count));
        }

        return new FocusTrend(result);
    }

    private static TimeSpan Average(IEnumerable<TimeSpan> values)
    {
        List<TimeSpan> list = values.ToList();
        return list.Count == 0 ? TimeSpan.Zero : TimeSpan.FromTicks((long)list.Average(value => value.Ticks));
    }
}
