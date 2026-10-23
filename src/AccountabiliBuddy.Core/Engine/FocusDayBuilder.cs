using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Link;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Builds <see cref="LinkFocusDay"/>: a day's focus as the day bar and History count it. Sessions the user started
/// and ones added by hand count; omitted sessions and old scheduled blocks don't. Each session is cut at the day's
/// edges (local midnight), so one across midnight counts on both days. The session running now counts until now; an
/// older session without an end stopped with the app at an unknown time and is left out. Planned minutes are the day
/// plan's Watch parts. Time the user stepped away or marked distracted in History doesn't count: an ended session
/// is sent in the pieces without it (as in sync files, so the phone's own sums agree), and marked stretches go with
/// the drift marks so they show red there too.
/// </summary>
public static class FocusDayBuilder
{
    private const int WeekDays = 7;

    /// <summary>Coming back to the same site within this long is one visit (as on the day bar; sync files too).</summary>
    internal static readonly TimeSpan DriftGap = TimeSpan.FromMinutes(2);

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
            IReadOnlyList<LocalWindow> pieces = session.Running || session.Away.Count == 0
                ? [session.Window]
                : DayTimeline.Subtract([session.Window], session.Away);
            foreach (LocalWindow piece in pieces)
            {
                if (DayTimeline.Clip(piece, today) is not LocalWindow inDay)
                {
                    continue;
                }

                DateTimeOffset? end = session.Running ? null : At(inDay.End, zone);
                sessions.Add(new LinkFocusSession(At(inDay.Start, zone), end, session.Added ? null : profileName(session.ProfileId), session.Added));
            }
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

        // Marked distracted in History: red on the phone's Focus row like drift.
        foreach (AwayStretch marked in counted.SelectMany(session => session.Distracted))
        {
            var window = new LocalWindow(Local(marked.From, zone), Local(marked.To, zone));
            if (DayTimeline.Clip(window, today) is LocalWindow inDay)
            {
                marks.Add(new LinkWindow(At(inDay.Start, zone), At(inDay.End, zone)));
            }
        }

        marks = marks.OrderBy(mark => mark.Start).ToList();

        var parts = plan.PartsIn(today)
            .Select(part => new LinkPlanPart(part.Name, part.Category.Name, part.Category.Color, KindOf(part.Category.Behavior),
                At(part.Window.Start, zone), At(part.Window.End, zone)))
            .ToList();

        var week = new List<LinkDayTotal>();
        for (int offset = WeekDays - 1; offset >= 0; offset--)
        {
            DateOnly date = day.AddDays(-offset);
            LocalWindow window = DayWindow(date);
            week.Add(new LinkDayTotal(date, Minutes(FocusedWindows(counted), window), Minutes(PlannedIn(plan, window), window)));
        }

        LinkDayTotal todayTotal = week[^1];
        return new LinkFocusDay(day, parts, sessions, marks, todayTotal.FocusedMinutes, todayTotal.PlannedMinutes, week);
    }

    /// <summary>The sessions that count, in local time; the running one runs until now.</summary>
    private static List<CountedSession> Counted(
        IReadOnlyList<SessionRecord> records, Func<DateTimeOffset, bool> isOmitted, bool sessionRunning, DateTimeOffset now, TimeZoneInfo zone)
    {
        return CountedRecords(records, isOmitted, sessionRunning, now)
            .Select(counted => new CountedSession(new LocalWindow(Local(counted.Record.StartedAt, zone), Local(counted.End, zone)),
                counted.Record.ProfileId, counted.Record.Mode == SessionMode.Added, counted.Running,
                AwayIn(counted.Record, counted.End, zone),
                (counted.Record.Away ?? []).Where(stretch => stretch.Kind == AwayKind.Distracted).ToList()))
            .ToList();
    }

    /// <summary>The record's away stretches in local time, cut at the session's end (one still going ends there).</summary>
    private static IReadOnlyList<LocalWindow> AwayIn(SessionRecord record, DateTimeOffset end, TimeZoneInfo zone)
    {
        return (record.Away ?? [])
            .Where(stretch => stretch.From < end)
            .Select(stretch => new LocalWindow(Local(stretch.From, zone), Local(stretch.To < end ? stretch.To : end, zone)))
            .ToList();
    }

    /// <summary>The sessions' windows without the time the user was away.</summary>
    private static IEnumerable<LocalWindow> FocusedWindows(IEnumerable<CountedSession> sessions)
    {
        return sessions.SelectMany(session => session.Away.Count == 0
            ? [session.Window]
            : DayTimeline.Subtract([session.Window], session.Away));
    }

    /// <summary>
    /// The rule for which recorded sessions count, shared with <see cref="Sync.SyncFileBuilder"/>: not omitted ones or
    /// old scheduled blocks, and of the sessions without an end only the newest the app recorded, while it runs.
    /// </summary>
    /// <returns>In the order of <paramref name="records"/>; the running one ends at <paramref name="now"/>.</returns>
    internal static List<(SessionRecord Record, DateTimeOffset End, bool Running)> CountedRecords(
        IReadOnlyList<SessionRecord> records, Func<DateTimeOffset, bool> isOmitted, bool sessionRunning, DateTimeOffset now)
    {
        // The session running now is the newest one the app recorded (an added one never runs).
        int newestRecorded = -1;
        for (int i = 0; i < records.Count && newestRecorded < 0; i++)
        {
            newestRecorded = records[i].Mode == SessionMode.Added ? -1 : i;
        }

        var counted = new List<(SessionRecord Record, DateTimeOffset End, bool Running)>();
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
                    counted.Add((record, ended, Running: false));
                }
            }
            else if (i == newestRecorded && sessionRunning && now > record.StartedAt)
            {
                counted.Add((record, now, Running: true));
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
            CategoryBehavior.Watch or CategoryBehavior.Comms => "watch",
            CategoryBehavior.Sleep => "sleep",
            CategoryBehavior.PreWork => "prework",
            _ => "rest",
        };
    }

    private static LocalWindow DayWindow(DateOnly date)
    {
        DateTime start = date.ToDateTime(TimeOnly.MinValue);
        return new LocalWindow(start, start.AddDays(1));
    }

    internal static DateTime Local(DateTimeOffset time, TimeZoneInfo zone)
    {
        return TimeZoneInfo.ConvertTime(time, zone).DateTime;
    }

    internal static DateTimeOffset At(DateTime local, TimeZoneInfo zone)
    {
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private sealed record CountedSession(
        LocalWindow Window, Guid? ProfileId, bool Added, bool Running, IReadOnlyList<LocalWindow> Away, IReadOnlyList<AwayStretch> Distracted);
}
