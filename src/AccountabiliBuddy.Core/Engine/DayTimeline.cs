using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Works out one day at a glance for the day bar: which stretch of time "today" covers and the focus time the
/// schedule plans in it. All times are local wall-clock times.
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

    /// <summary>
    /// Every planned focus window overlapping <paramref name="range"/>, cut to it, with time off taken out,
    /// earliest first. Overlapping blocks stay separate entries (see <see cref="Union"/> for the merged time).
    /// </summary>
    public static IReadOnlyList<PlannedFocus> Planned(WeeklySchedule schedule, LocalWindow range)
    {
        var occurrences = new List<PlannedFocus>();

        // Start a day early so a block crossing midnight into the range is included.
        for (DateTime date = range.Start.Date.AddDays(-1); date < range.End; date = date.AddDays(1))
        {
            foreach (ScheduleBlock block in schedule.Blocks.Where(block => block.Day == date.DayOfWeek))
            {
                DateTime start = date + block.Start.ToTimeSpan();
                DateTime end = date + block.End.ToTimeSpan();
                occurrences.Add(new PlannedFocus(block.ProfileId, new LocalWindow(start, block.CrossesMidnight ? end.AddDays(1) : end)));
            }
        }

        occurrences.AddRange(schedule.OneOffBlocks.Select(block => new PlannedFocus(block.ProfileId, block.GetWindow())));

        List<LocalWindow> timeOff = schedule.TimeOff.Select(off => off.GetWindow()).ToList();
        return occurrences
            .SelectMany(focus => Subtract(focus.Window, timeOff).Select(window => focus with { Window = window }))
            .Select(focus => Clip(focus.Window, range) is LocalWindow clipped ? focus with { Window = clipped } : null)
            .OfType<PlannedFocus>()
            .OrderBy(focus => focus.Window.Start)
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

    /// <summary>The part of <paramref name="window"/> inside <paramref name="range"/>, or null if none.</summary>
    public static LocalWindow? Clip(LocalWindow window, LocalWindow range)
    {
        DateTime start = Max(window.Start, range.Start);
        DateTime end = Min(window.End, range.End);
        return end > start ? new LocalWindow(start, end) : null;
    }

    private static IEnumerable<LocalWindow> Subtract(LocalWindow window, IReadOnlyList<LocalWindow> holes)
    {
        var pieces = new List<LocalWindow> { window };
        foreach (LocalWindow hole in holes)
        {
            pieces = pieces
                .SelectMany(piece => new[]
                {
                    new LocalWindow(piece.Start, Min(piece.End, hole.Start)),
                    new LocalWindow(Max(piece.Start, hole.End), piece.End),
                })
                .Where(piece => piece.End > piece.Start)
                .ToList();
        }

        return pieces;
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
