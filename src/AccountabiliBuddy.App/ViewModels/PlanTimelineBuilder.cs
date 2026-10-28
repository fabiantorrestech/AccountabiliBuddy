using System.Globalization;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Builds the Plan page's timeline for one date from the parts the guards see (<see cref="DayWorkWindows"/>): a Wake row
/// for the night that ends that morning, a row per part, a Bedtime row for the night that starts that evening. Work
/// parts that follow each other (with lunch or a break in between) become one "Work" row that expands into deep-work
/// stretches (the work minus breaks, comms windows and calendar events), the events themselves, the breaks, and on
/// today a "Now" line. Times are 12-hour.
/// </summary>
public static class PlanTimelineBuilder
{
    // Parts closer than this run into one work row.
    private static readonly TimeSpan Bridge = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ShortestStretch = TimeSpan.FromMinutes(15);

    /// <param name="entries">The date's events from the visible calendars.</param>
    /// <param name="focused">Counted focus today, shared out over the deep-work stretches that have started.</param>
    /// <param name="goal">The deep-work goal, shown under the first work row; zero hides it.</param>
    public static IReadOnlyList<PlanTimelineRowViewModel> Build(
        DayPlan plan,
        DateOnly date,
        DateTime now,
        IReadOnlyList<CalendarEntry> entries,
        TimeSpan focused,
        TimeSpan goal,
        Func<DateOnly, SleepTimes?>? nightFallback,
        Action<PlanTimelineRowViewModel> open)
    {
        DateTime dayStart = date.ToDateTime(TimeOnly.MinValue);
        var day = new LocalWindow(dayStart, dayStart.AddDays(1));
        bool isToday = DateOnly.FromDateTime(now) == date;
        IReadOnlyList<PlannedPart> parts = DayWorkWindows.PartsIn(plan, day, now, other => other == date ? focused : TimeSpan.Zero, nightFallback);
        IReadOnlyList<(CalendarEvent Event, LocalWindow Window)> timed = entries
            .Where(entry => !entry.Event.IsAllDay)
            .Select(entry => (entry.Event, Window: DayTimeline.Clip(Window(entry.Event), day)))
            .Where(pair => pair.Window is not null)
            .Select(pair => (pair.Event, pair.Window!.Value))
            .ToList();

        var rows = new List<PlanTimelineRowViewModel>();
        List<PlannedPart>? group = null;
        var trailing = new List<PlannedPart>();
        bool goalShown = false;

        DateTime LastEnd() => trailing.Count > 0 ? trailing[^1].Window.End : group![^1].Window.End;

        void Flush()
        {
            if (group is not null)
            {
                rows.Add(WorkRow(group, goalShown ? TimeSpan.Zero : goal, timed, isToday, now, focused, open));
                goalShown = true;
                group = null;
            }

            foreach (PlannedPart rest in trailing)
            {
                rows.Add(PartRow(rest, day, open));
            }

            trailing.Clear();
        }

        foreach (PlannedPart part in parts)
        {
            CategoryBehavior behavior = part.Category.Behavior;
            if (behavior.IsWork())
            {
                if (group is not null && part.Window.Start - LastEnd() <= Bridge)
                {
                    group.AddRange(trailing);
                    trailing.Clear();
                    group.Add(part);
                }
                else
                {
                    Flush();
                    group = [part];
                }
            }
            else if (behavior == CategoryBehavior.Rest && group is not null && part.Window.Start - LastEnd() <= Bridge)
            {
                trailing.Add(part);
            }
            else
            {
                Flush();
                PlanTimelineRowViewModel? row = behavior == CategoryBehavior.Sleep ? SleepRow(part, day, open) : PartRow(part, day, open);
                if (row is not null)
                {
                    rows.Add(row);
                }
            }
        }

        Flush();
        return rows;
    }

    /// <summary>"1:00 PM".</summary>
    public static string Clock(DateTime time) => time.ToString("h:mm tt", CultureInfo.InvariantCulture);

    /// <summary>"2h", "1h 10m" or "45m".</summary>
    public static string Span(TimeSpan span)
    {
        int minutes = (int)Math.Round(span.TotalMinutes);
        int hours = minutes / 60;
        int rest = minutes % 60;
        return hours == 0 ? $"{rest}m" : rest == 0 ? $"{hours}h" : $"{hours}h {rest}m";
    }

    // A calendar event as a window (an event with no length counts a minute).
    private static LocalWindow Window(CalendarEvent calendarEvent)
    {
        return new LocalWindow(calendarEvent.LocalStart, calendarEvent.LocalEnd > calendarEvent.LocalStart ? calendarEvent.LocalEnd : calendarEvent.LocalStart.AddMinutes(1));
    }

    private static PlanTimelineRowViewModel WorkRow(
        List<PlannedPart> group,
        TimeSpan goal,
        IReadOnlyList<(CalendarEvent Event, LocalWindow Window)> timed,
        bool isToday,
        DateTime now,
        TimeSpan focused,
        Action<PlanTimelineRowViewModel> open)
    {
        PlannedPart first = group.FirstOrDefault(part => part.Category.Behavior == CategoryBehavior.Watch) ?? group[0];
        var span = new LocalWindow(group[0].Window.Start, group[^1].Window.End);
        List<LocalWindow> others = group.Where(part => part.Category.Behavior != CategoryBehavior.Watch).Select(part => part.Window).ToList();
        List<(CalendarEvent Event, LocalWindow Window)> events = timed
            .Where(pair => pair.Window.End > span.Start && pair.Window.Start < span.End)
            .ToList();

        // Deep work: the Watch parts minus breaks, comms windows and events.
        List<LocalWindow> stretches = DayTimeline.Subtract(
                group.Where(part => part.Category.Behavior == CategoryBehavior.Watch).Select(part => part.Window),
                others.Concat(events.Select(pair => pair.Window)))
            .Where(window => window.End - window.Start >= ShortestStretch)
            .ToList();

        var subRows = new List<(DateTime At, PlanSubRowViewModel Row)>();
        TimeSpan remaining = isToday ? focused : TimeSpan.Zero;
        foreach (LocalWindow stretch in stretches)
        {
            TimeSpan elapsed = isToday && now > stretch.Start ? Min(now, stretch.End) - stretch.Start : TimeSpan.Zero;
            TimeSpan done = Min(remaining, elapsed);
            remaining -= done;
            subRows.Add((stretch.Start, new PlanSubRowViewModel
            {
                TimeText = Clock(stretch.Start),
                Title = "Deep work",
                RightText = done >= TimeSpan.FromMinutes(1) ? $"{Span(done)} done" : $"Until {Clock(stretch.End)}",
            }));
        }

        foreach (PlannedPart part in group.Where(part => part.Category.Behavior != CategoryBehavior.Watch))
        {
            subRows.Add((part.Window.Start, new PlanSubRowViewModel
            {
                TimeText = Clock(part.Window.Start),
                Title = part.Name,
                RightText = Span(part.Window.End - part.Window.Start),
            }));
        }

        foreach ((CalendarEvent calendarEvent, LocalWindow window) in events)
        {
            DateTime at = window.Start < span.Start ? span.Start : window.Start;
            subRows.Add((at, new PlanSubRowViewModel
            {
                TimeText = Clock(at),
                Title = calendarEvent.Title,
                RightText = "Calendar",
                IsEvent = true,
            }));
        }

        List<PlanSubRowViewModel> ordered = subRows.OrderBy(pair => pair.At).Select(pair => pair.Row).ToList();
        if (isToday && now >= span.Start && now < span.End)
        {
            int index = subRows.OrderBy(pair => pair.At).TakeWhile(pair => pair.At <= now).Count();
            ordered.Insert(index, new PlanSubRowViewModel { IsNowLine = true, Title = $"Now · {Clock(now)}" });
        }

        string until = $"Until {Clock(span.End)}";
        return new PlanTimelineRowViewModel(open)
        {
            TimeText = Clock(span.Start),
            Title = first.Name,
            SubText = goal > TimeSpan.Zero ? $"{until} · deep work goal {Span(goal)}" : until,
            SubRows = ordered,
            PartId = first.Part.Id,
        };
    }

    // A part that isn't work: a break, a pre-work window.
    private static PlanTimelineRowViewModel PartRow(PlannedPart part, LocalWindow day, Action<PlanTimelineRowViewModel> open)
    {
        TimeSpan length = part.Window.End - part.Window.Start;
        string kind = part.Part.IsFlexible
            ? $"Flexible · {part.Part.FlexMinutes} min"
            : $"Until {ClockEnd(part.Window.End, day)} · {Span(length)}";
        return new PlanTimelineRowViewModel(open)
        {
            TimeText = Clock(part.Window.Start),
            Title = part.Name,
            SubText = kind,
            PartId = part.Part.Id,
        };
    }

    // The night: its end is a Wake row, its start a Bedtime row; a sleep that starts and ends inside the day is one row.
    private static PlanTimelineRowViewModel? SleepRow(PlannedPart part, LocalWindow day, Action<PlanTimelineRowViewModel> open)
    {
        bool endsInDay = part.Window.End < day.End;
        bool startsInDay = part.Window.Start > day.Start;
        if (endsInDay && !(startsInDay && part.Window.End.Hour >= 12))
        {
            return new PlanTimelineRowViewModel(open)
            {
                TimeText = Clock(part.Window.End),
                Title = "Wake",
                SubText = part.Part.WakeByAlarm ? "Wake-up alarm" : "Wake-up time",
                IsSleep = true,
                PartId = part.Part.Id,
            };
        }

        if (!endsInDay && startsInDay)
        {
            DateTime wake = day.Start + part.Part.End.ToTimeSpan();
            return new PlanTimelineRowViewModel(open)
            {
                TimeText = Clock(part.Window.Start),
                Title = "Bedtime",
                SubText = part.Part.WakeByAlarm ? "Wakes with your alarm" : $"Sleep until {Clock(wake)}",
                IsSleep = true,
                PartId = part.Part.Id,
            };
        }

        if (endsInDay && startsInDay)
        {
            return new PlanTimelineRowViewModel(open)
            {
                TimeText = Clock(part.Window.Start),
                Title = part.Name,
                SubText = $"Until {Clock(part.Window.End)} · {Span(part.Window.End - part.Window.Start)}",
                IsSleep = true,
                PartId = part.Part.Id,
            };
        }

        return null;
    }

    private static string ClockEnd(DateTime end, LocalWindow day) => end >= day.End ? "12:00 AM" : Clock(end);

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}
