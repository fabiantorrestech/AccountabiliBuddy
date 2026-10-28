using System.Globalization;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// The Plan helper (Plan page): reads the day plan, the calendar's events, the deep-work goal and today's focus so far and
/// says what looks wrong, as a short list of suggestions. Pure: no clock, no storage, no UI; the caller passes "now" and
/// recomputes when the plan, the calendar or the day changes. Three rules:
/// <list type="number">
/// <item>A coming day's calendar covers at least half of its work: offer to move the day's biggest work part, whole, into the
/// largest free stretch of the waking day (a one-off day for that date), or only say so when nothing is free.</item>
/// <item>Today's goal no longer fits in the work time left (after the day's calendar): say so; no plan edit.</item>
/// <item>A day of next week follows no day type: say so; the app opens Change a day.</item>
/// </list>
/// Moving is never applied here: <see cref="PlanSuggestion.Edit"/> is a proposal the app puts through the normal day plan
/// edit path, with its lock during a session and its wait.
/// </summary>
public static class PlanHelper
{
    /// <summary>How many coming days the first rule looks at (tomorrow and the six after).</summary>
    public const int DaysAhead = 7;

    private static readonly TimeSpan ShortestWork = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan Quarter = TimeSpan.FromMinutes(15);

    // A move never lands before this or ends after this; a day's work isn't moved into the night.
    private static readonly TimeSpan EarliestMove = TimeSpan.FromHours(6);
    private static readonly TimeSpan LatestMoveEnd = TimeSpan.FromHours(21);

    /// <param name="events">Events of the visible calendars; all-day events are ignored (they don't block hours).</param>
    /// <param name="now">Local wall-clock time.</param>
    /// <param name="goal">Today's deep-work goal.</param>
    /// <param name="focusedToday">Counted focus so far today.</param>
    /// <param name="nightFallback">The night before a date when a Budget day's plan has no Sleep part; null = none.</param>
    /// <returns>At most one suggestion per rule and day, today's first, then by date.</returns>
    public static IReadOnlyList<PlanSuggestion> Suggest(
        DayPlan plan,
        IReadOnlyList<CalendarEvent> events,
        DateTime now,
        TimeSpan goal,
        TimeSpan focusedToday,
        Func<DateOnly, SleepTimes?>? nightFallback = null)
    {
        var suggestions = new List<PlanSuggestion>();
        DateOnly today = DateOnly.FromDateTime(now);

        if (GoalDoesNotFit(plan, events, now, today, goal, focusedToday, nightFallback) is PlanSuggestion fit)
        {
            suggestions.Add(fit);
        }

        for (int ahead = 1; ahead <= DaysAhead; ahead++)
        {
            if (WorkCoveredByCalendar(plan, events, now, today.AddDays(ahead), nightFallback) is PlanSuggestion move)
            {
                suggestions.Add(move);
            }
        }

        if (FirstDayWithoutType(plan, today) is PlanSuggestion missing)
        {
            suggestions.Add(missing);
        }

        return suggestions;
    }

    // Rule 2.
    private static PlanSuggestion? GoalDoesNotFit(DayPlan plan, IReadOnlyList<CalendarEvent> events, DateTime now, DateOnly today, TimeSpan goal, TimeSpan focusedToday, Func<DateOnly, SleepTimes?>? nightFallback)
    {
        TimeSpan needed = goal - focusedToday;
        if (needed <= TimeSpan.Zero)
        {
            return null;
        }

        LocalWindow day = DayOf(today);
        List<LocalWindow> work = WorkParts(plan, day, now, date => date == today ? focusedToday : TimeSpan.Zero, nightFallback)
            .Select(part => part.Window)
            .ToList();
        // Nothing of today's work is left (or there is none): there is nothing to fit the goal into, so nothing to say.
        if (!work.Any(window => window.End > now))
        {
            return null;
        }

        IEnumerable<LocalWindow> ahead = work.Select(window => DayTimeline.Clip(window, new LocalWindow(now, day.End))).OfType<LocalWindow>();
        TimeSpan left = Length(DayTimeline.Subtract(ahead, BusyOn(events, today)));
        if (left >= needed)
        {
            return null;
        }

        return new PlanSuggestion(PlanSuggestionKind.GoalDoesNotFit, today,
            $"{Span(goal)} goal, {Span(left)} of work left. Start now or lower today's goal?");
    }

    // Rule 1.
    private static PlanSuggestion? WorkCoveredByCalendar(DayPlan plan, IReadOnlyList<CalendarEvent> events, DateTime now, DateOnly date, Func<DateOnly, SleepTimes?>? nightFallback)
    {
        LocalWindow day = DayOf(date);
        IReadOnlyList<PlannedPart> work = WorkParts(plan, day, now, null, nightFallback);
        IReadOnlyList<LocalWindow> workWindows = DayTimeline.Union(work.Select(part => part.Window));
        TimeSpan total = Length(workWindows);
        if (total < ShortestWork)
        {
            return null;
        }

        IReadOnlyList<LocalWindow> busy = BusyOn(events, date);
        TimeSpan free = Length(DayTimeline.Subtract(workWindows, busy));
        if ((total - free) * 2 < total)
        {
            return null;
        }

        DateTime? firstBusy = busy
            .SelectMany(window => workWindows.Select(workWindow => DayTimeline.Clip(window, workWindow)).OfType<LocalWindow>())
            .Select(window => (DateTime?)window.Start)
            .Min();
        string start = firstBusy is DateTime first ? Clock(first) : "the start of work";
        string weekday = date.DayOfWeek.ToString();

        PlanEdit? edit = MoveBiggestWorkPart(plan, date, work, busy, nightFallback, out string? period);
        return edit is null
            ? new PlanSuggestion(PlanSuggestionKind.MoveWork, date, $"{weekday}'s calendar is full from {start}.")
            : new PlanSuggestion(PlanSuggestionKind.MoveWork, date, $"{weekday}'s calendar is full from {start}. Move work to the {period}?", edit);
    }

    // The edit for rule 1: the day's longest work part into the largest free stretch of the waking day.
    private static PlanEdit? MoveBiggestWorkPart(DayPlan plan, DateOnly date, IReadOnlyList<PlannedPart> work, IReadOnlyList<LocalWindow> busy, Func<DateOnly, SleepTimes?>? nightFallback, out string? period)
    {
        period = null;
        DayTemplate template = plan.TemplateFor(date);
        if (template.Mode != DayMode.Schedule)
        {
            return null;
        }

        PlannedPart? biggest = work
            .Where(part => !part.Part.CrossesMidnight && !part.Part.IsFlexible)
            .OrderByDescending(part => Length(part.Window))
            .FirstOrDefault();
        if (biggest is null)
        {
            return null;
        }

        PlanPart part = biggest.Part;
        TimeSpan length = TimeSpan.FromMinutes(part.EndMinutes - (int)part.Start.ToTimeSpan().TotalMinutes);
        LocalWindow day = DayOf(date);
        TimeSpan stillFree = Length(DayTimeline.Subtract([biggest.Window], busy));

        // Free: from waking to bedtime (never before 6 AM or past 9 PM), outside every other part and every event.
        DateTime windowFrom = date.ToDateTime(TimeOnly.MinValue) + EarliestMove;
        DateTime windowUntil = date.ToDateTime(TimeOnly.MinValue) + LatestMoveEnd;
        IEnumerable<LocalWindow> holes = plan.PartsIn(day)
            .Where(other => other.Part.Id != part.Id)
            .Select(other => other.Window)
            .Concat(busy);
        // The whole part must fit: moving never shortens a day's work.
        LocalWindow? stretch = DayTimeline.Subtract([new LocalWindow(windowFrom, windowUntil)], holes)
            .Where(window => window.End - RoundUp(window.Start) >= length)
            .OrderByDescending(window => window.End - window.Start)
            .ThenBy(window => window.Start)
            .Cast<LocalWindow?>()
            .FirstOrDefault();
        if (stretch is not LocalWindow target)
        {
            return null;
        }

        DateTime newStart = RoundUp(target.Start);
        // Only worth a suggestion when it gains something over where the part is now.
        if (length <= stillFree + Quarter)
        {
            return null;
        }

        TimeOnly movedStart = TimeOnly.FromTimeSpan(newStart.TimeOfDay);
        TimeOnly movedEnd = TimeOnly.FromTimeSpan((newStart + length).TimeOfDay);
        DayTemplate? changed = WithPartMoved(plan, date, part, movedStart, movedEnd);
        if (changed is null || DayPlanValidator.Validate(changed, plan.Categories) is not null)
        {
            return null;
        }

        period = newStart.Hour < 12 ? "morning" : newStart.Hour < 17 ? "afternoon" : "evening";
        string? note = plan.OneOffDays.FirstOrDefault(day => day.Date == date)?.Note ?? "Work moved";
        return new PlanEdit(date, changed, note);
    }

    // The date's stored template with the part at its new times: a part of the template, or the one piece of a work window.
    private static DayTemplate? WithPartMoved(DayPlan plan, DateOnly date, PlanPart part, TimeOnly start, TimeOnly end)
    {
        DayTemplate stored = plan.OneOffDays.FirstOrDefault(day => day.Date == date)?.Template
            ?? (plan.Cycle.TemplateIdFor(date) is Guid id ? plan.TemplateById(id) : null)
            ?? plan.TemplateById(DayPlan.OffDayTemplateId)
            ?? DayTemplate.Empty;
        if (stored.Parts.Any(own => own.Id == part.Id))
        {
            return stored with { Parts = stored.Parts.Select(own => own.Id == part.Id ? own with { Start = start, End = end } : own).ToList() };
        }

        // The part stands in for a work window: with one piece, the window itself moves.
        bool oneWindowPiece = stored.WorkWindow is not null && plan.TemplateFor(date).Parts
            .Count(own => plan.Category(own.CategoryId)?.Behavior.IsWork() == true) == 1;
        return oneWindowPiece ? stored with { WorkWindow = new WorkWindow(start, end) } : null;
    }

    // Rule 3: the first day of next week whose weekday follows no day type and has no plan of its own.
    private static PlanSuggestion? FirstDayWithoutType(DayPlan plan, DateOnly today)
    {
        DateOnly monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7)).AddDays(7);
        for (int offset = 0; offset < 7; offset++)
        {
            DateOnly date = monday.AddDays(offset);
            if (plan.Cycle.TemplateIdFor(date) is null && plan.OneOffDays.All(day => day.Date != date))
            {
                return new PlanSuggestion(PlanSuggestionKind.NoDayType, date, $"Next {date.DayOfWeek} has no day type yet.");
            }
        }

        return null;
    }

    // The Watch parts the guards see in the range (work, not Comms: those are mail and chat, not deep work).
    private static IReadOnlyList<PlannedPart> WorkParts(DayPlan plan, LocalWindow range, DateTime now, Func<DateOnly, TimeSpan>? focusedOn, Func<DateOnly, SleepTimes?>? nightFallback)
    {
        return DayWorkWindows.PartsIn(plan, range, now, focusedOn, nightFallback)
            .Where(part => part.Category.Behavior == CategoryBehavior.Watch)
            .ToList();
    }

    // Timed events as windows on the date (an event with no length counts a minute).
    private static IReadOnlyList<LocalWindow> BusyOn(IReadOnlyList<CalendarEvent> events, DateOnly date)
    {
        LocalWindow day = DayOf(date);
        return DayTimeline.Union(events
            .Where(calendarEvent => !calendarEvent.IsAllDay && calendarEvent.OccursOn(date))
            .Select(calendarEvent => DayTimeline.Clip(
                new LocalWindow(calendarEvent.LocalStart, calendarEvent.LocalEnd > calendarEvent.LocalStart ? calendarEvent.LocalEnd : calendarEvent.LocalStart.AddMinutes(1)), day))
            .OfType<LocalWindow>());
    }

    private static LocalWindow DayOf(DateOnly date)
    {
        DateTime start = date.ToDateTime(TimeOnly.MinValue);
        return new LocalWindow(start, start.AddDays(1));
    }

    private static TimeSpan Length(IEnumerable<LocalWindow> windows)
    {
        return windows.Aggregate(TimeSpan.Zero, (sum, window) => sum + (window.End - window.Start));
    }

    private static TimeSpan Length(LocalWindow window) => window.End - window.Start;

    private static DateTime RoundUp(DateTime time)
    {
        long ticks = (time.Ticks + Quarter.Ticks - 1) / Quarter.Ticks * Quarter.Ticks;
        return new DateTime(ticks);
    }

    /// <summary>"2h", "1h 20m" or "45m".</summary>
    private static string Span(TimeSpan span)
    {
        int minutes = (int)Math.Round(span.TotalMinutes);
        int hours = minutes / 60;
        int rest = minutes % 60;
        return hours == 0 ? $"{rest}m" : rest == 0 ? $"{hours}h" : $"{hours}h {rest}m";
    }

    /// <summary>"1:00 PM".</summary>
    private static string Clock(DateTime time) => time.ToString("h:mm tt", CultureInfo.InvariantCulture);
}
