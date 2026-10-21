using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Link;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Builds <see cref="LinkFocusDay"/>: a day's focus as the day bar and History count it. Sessions the user started
/// and ones added by hand count; omitted sessions and old scheduled blocks don't. Each session is cut at the day's
/// edges (local midnight), so one across midnight counts on both days. The session running now counts until now; an
/// older session without an end stopped with the app at an unknown time and is left out. Planned minutes are the day
/// plan's Watch parts.
/// </summary>
public static class FocusDayBuilder
{
    private const int WeekDays = 7;

    // Coming back to the same site within this long is one visit (as on the day bar).
    private static readonly TimeSpan DriftGap = TimeSpan.FromMinutes(2);

    /// <param name="records">Newest first (<see cref="SessionHistoryBuilder.Build"/>), covering the week and the day before it.</param>
    /// <param name="drift">Drift inside counted sessions on <paramref name="day"/> (omitted and scheduled time already left out).</param>
    /// <param name="sessionRunning">True while a session runs on the PC: the newest recorded session without an end is it.</param>
    public static LinkFocusDay Build(
        DateOnly day,
        IReadOnlyList<SessionRecord> records,
        IReadOnlyList<DriftStretch> drift,
        DayPlan plan,
        Func<DateTimeOffset, bool> isOmitted,
        Func<Guid?, string?> profileName,
        bool sessionRunning,
        DateTimeOffset now,
        TimeZoneInfo zone)
    {
        List<CountedSession> counted = Counted(records, isOmitted, sessionRunning, now, zone);
        LocalWindow today = DayWindow(day);

        var sessions = new List<LinkFocusSession>();
        foreach (CountedSession session in counted.OrderBy(session => session.Window.Start))
        {
            if (DayTimeline.Clip(session.Window, today) is not LocalWindow inDay)
            {
                continue;
            }

            DateTimeOffset? end = session.Running ? null : At(inDay.End, zone);
            sessions.Add(new LinkFocusSession(At(inDay.Start, zone), end, session.Added ? null : profileName(session.ProfileId), session.Added));
        }

        var marks = new List<LinkWindow>();
        foreach (DriftStretch mark in DriftMarks.Merge(drift, DriftGap))
        {
            var window = new LocalWindow(Local(mark.Start, zone), Local(mark.End, zone));
            if (DayTimeline.Clip(window, today) is LocalWindow inDay)
            {
                marks.Add(new LinkWindow(At(inDay.Start, zone), At(inDay.End, zone)));
            }
        }

        var parts = plan.PartsIn(today)
            .Select(part => new LinkPlanPart(part.Name, part.Category.Name, part.Category.Color, KindOf(part.Category.Behavior),
                At(part.Window.Start, zone), At(part.Window.End, zone)))
            .ToList();

        var week = new List<LinkDayTotal>();
        for (int offset = WeekDays - 1; offset >= 0; offset--)
        {
            DateOnly date = day.AddDays(-offset);
            LocalWindow window = DayWindow(date);
            week.Add(new LinkDayTotal(date, Minutes(counted.Select(session => session.Window), window), Minutes(PlannedIn(plan, window), window)));
        }

        LinkDayTotal todayTotal = week[^1];
        return new LinkFocusDay(day, parts, sessions, marks, todayTotal.FocusedMinutes, todayTotal.PlannedMinutes, week);
    }

    /// <summary>The sessions that count, in local time; the running one runs until now.</summary>
    private static List<CountedSession> Counted(
        IReadOnlyList<SessionRecord> records, Func<DateTimeOffset, bool> isOmitted, bool sessionRunning, DateTimeOffset now, TimeZoneInfo zone)
    {
        // The session running now is the newest one the app recorded (an added one never runs).
        int newestRecorded = -1;
        for (int i = 0; i < records.Count && newestRecorded < 0; i++)
        {
            newestRecorded = records[i].Mode == SessionMode.Added ? -1 : i;
        }

        var counted = new List<CountedSession>();
        for (int i = 0; i < records.Count; i++)
        {
            SessionRecord record = records[i];
            if (isOmitted(record.StartedAt) || record.Mode == SessionMode.Scheduled)
            {
                continue;
            }

            if (record.EndedAt is DateTimeOffset ended)
            {
                if (ended > record.StartedAt)
                {
                    counted.Add(new CountedSession(new LocalWindow(Local(record.StartedAt, zone), Local(ended, zone)), record.ProfileId,
                        record.Mode == SessionMode.Added, Running: false));
                }
            }
            else if (i == newestRecorded && sessionRunning && now > record.StartedAt)
            {
                counted.Add(new CountedSession(new LocalWindow(Local(record.StartedAt, zone), Local(now, zone)), record.ProfileId,
                    Added: false, Running: true));
            }
        }

        return counted;
    }

    private static IEnumerable<LocalWindow> PlannedIn(DayPlan plan, LocalWindow day)
    {
        return DayTimeline.Planned(plan, day).Select(focus => focus.Window);
    }

    /// <summary>Whole minutes of the windows inside <paramref name="range"/>, overlaps counted once.</summary>
    private static int Minutes(IEnumerable<LocalWindow> windows, LocalWindow range)
    {
        IEnumerable<LocalWindow> clipped = windows.Select(window => DayTimeline.Clip(window, range)).OfType<LocalWindow>();
        TimeSpan total = DayTimeline.Union(clipped).Aggregate(TimeSpan.Zero, (sum, window) => sum + (window.End - window.Start));
        return (int)Math.Round(total.TotalMinutes);
    }

    private static string KindOf(CategoryBehavior behavior)
    {
        return behavior switch
        {
            CategoryBehavior.Watch => "watch",
            CategoryBehavior.Sleep => "sleep",
            _ => "rest",
        };
    }

    private static LocalWindow DayWindow(DateOnly date)
    {
        DateTime start = date.ToDateTime(TimeOnly.MinValue);
        return new LocalWindow(start, start.AddDays(1));
    }

    private static DateTime Local(DateTimeOffset time, TimeZoneInfo zone)
    {
        return TimeZoneInfo.ConvertTime(time, zone).DateTime;
    }

    private static DateTimeOffset At(DateTime local, TimeZoneInfo zone)
    {
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private sealed record CountedSession(LocalWindow Window, Guid? ProfileId, bool Added, bool Running);
}
