using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Works out one day at a glance for the day bar: which stretch of time "today" covers and the work the day plan
/// has in it. All times are local wall-clock times.
/// </summary>
public static class DayTimeline
{
    /// <summary>
    /// The day <paramref name="now"/> belongs to, from <paramref name="dayStart"/> to <paramref name="dayEnd"/>.
    /// When the end is not after the start, the day ends on the next date, and until that end the time still
    /// belongs to the previous day (1 AM with a 2 AM end is still yesterday's day).
    /// </summary>
    public static LocalWindow DayContaining(DateTime now, TimeOnly dayStart, TimeOnly dayEnd)
    {
        bool endsNextDay = dayEnd <= dayStart;
        DateTime date = now.Date;
        if (endsNextDay && TimeOnly.FromDateTime(now) < dayEnd)
        {
            date = date.AddDays(-1);
        }

        DateTime start = date + dayStart.ToTimeSpan();
        DateTime end = date + dayEnd.ToTimeSpan();
        return new LocalWindow(start, endsNextDay ? end.AddDays(1) : end);
    }

    /// <summary>Midnight to midnight of the date <paramref name="now"/> is on.</summary>
    public static LocalWindow FullDay(DateTime now)
    {
        return new LocalWindow(now.Date, now.Date.AddDays(1));
    }

    /// <summary>The whole calendar dates <paramref name="day"/> touches, from the midnight before it to the one after.</summary>
    public static LocalWindow CalendarSpan(LocalWindow day)
    {
        DateTime end = day.End == day.End.Date ? day.End : day.End.Date.AddDays(1);
        return new LocalWindow(day.Start.Date, end);
    }

    /// <summary>
    /// Grows <paramref name="day"/> to whole hours so it covers every window in <paramref name="content"/>
    /// (e.g. a block starting before the usual day start), but never past <paramref name="limit"/>.
    /// </summary>
    public static LocalWindow FitTo(LocalWindow day, IEnumerable<LocalWindow> content, LocalWindow limit)
    {
        DateTime start = day.Start;
        DateTime end = day.End;
        foreach (LocalWindow window in content)
        {
            if (window.Start < start)
            {
                start = FloorToHour(window.Start);
            }

            if (window.End > end)
            {
                end = CeilingToHour(window.End);
            }
        }

        return new LocalWindow(Max(start, limit.Start), Min(end, limit.End));
    }

    /// <summary>The day plan's work parts (Watch and Comms: Work, Extra Work, Comms…) overlapping <paramref name="range"/>, cut to it, earliest first.</summary>
    public static IReadOnlyList<PlannedFocus> Planned(DayPlan plan, LocalWindow range)
    {
        return plan.PartsIn(range)
            .Where(part => part.Category.Behavior.IsWork())
            .Select(part => new PlannedFocus(part.Name, part.Category.Color, part.Window))
            .ToList();
    }

    /// <summary>
    /// How the planned work in <paramref name="range"/> splits by part name (a part's own name, else its category's,
    /// e.g. Work 7h, Extra Work 2h), in the order each name first comes in the day.
    /// </summary>
    public static IReadOnlyList<(string Name, TimeSpan Length)> PlannedByName(IReadOnlyList<PlannedFocus> planned, LocalWindow range)
    {
        return planned
            .GroupBy(focus => focus.Name)
            .Select(group => (group.Key, Union(group.Select(focus => Clip(focus.Window, range)).OfType<LocalWindow>())
                .Aggregate(TimeSpan.Zero, (sum, window) => sum + (window.End - window.Start))))
            .Where(entry => entry.Item2 > TimeSpan.Zero)
            .ToList();
    }

    /// <summary>Merges overlapping and touching windows, earliest first.</summary>
    public static IReadOnlyList<LocalWindow> Union(IEnumerable<LocalWindow> windows)
    {
        var merged = new List<LocalWindow>();
        foreach (LocalWindow window in windows.OrderBy(window => window.Start))
        {
            if (merged.Count > 0 && window.Start <= merged[^1].End)
            {
                LocalWindow last = merged[^1];
                merged[^1] = last with { End = Max(last.End, window.End) };
            }
            else
            {
                merged.Add(window);
            }
        }

        return merged;
    }

    /// <summary>
    /// What's left of <paramref name="windows"/> outside every one of <paramref name="holes"/>, earliest first (e.g. work
    /// time with no focus session).
    /// </summary>
    public static IReadOnlyList<LocalWindow> Subtract(IEnumerable<LocalWindow> windows, IEnumerable<LocalWindow> holes)
    {
        IReadOnlyList<LocalWindow> cuts = Union(holes);
        var left = new List<LocalWindow>();
        foreach (LocalWindow window in Union(windows))
        {
            DateTime from = window.Start;
            foreach (LocalWindow cut in cuts.Where(cut => cut.End > window.Start && cut.Start < window.End))
            {
                if (cut.Start > from)
                {
                    left.Add(new LocalWindow(from, cut.Start));
                }

                from = Max(from, cut.End);
            }

            if (window.End > from)
            {
                left.Add(new LocalWindow(from, window.End));
            }
        }

        return left;
    }

    /// <summary>The part of <paramref name="window"/> inside <paramref name="range"/>, or null if none.</summary>
    public static LocalWindow? Clip(LocalWindow window, LocalWindow range)
    {
        DateTime start = Max(window.Start, range.Start);
        DateTime end = Min(window.End, range.End);
        return end > start ? new LocalWindow(start, end) : null;
    }

    private static DateTime FloorToHour(DateTime time)
    {
        return time.Date.AddHours(time.Hour);
    }

    private static DateTime CeilingToHour(DateTime time)
    {
        DateTime floor = FloorToHour(time);
        return floor == time ? time : floor.AddHours(1);
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}
