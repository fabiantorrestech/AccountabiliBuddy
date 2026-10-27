using System.Globalization;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// The Setup DP Assistant's review page: plain-language bullets of how the buddy behaves through each kind of day under a
/// plan, worked out from the same places the guards read (<see cref="DayWorkWindows"/>, the plan's lunch, the nights and
/// the bedtime reminders), never from a copy of the rules. Times are 12-hour "h:mm tt" in any culture.
/// </summary>
public static class DpAssistantReview
{
    /// <summary>One entry per day type the cycle uses (Workday first when it is used), each from a sample date of that type.</summary>
    public static IReadOnlyList<DpReviewDay> Describe(DayPlan plan, DateOnly today, DpReviewContext context)
    {
        // One-off days replace single dates; the review is about the kinds of day the weeks give.
        DayPlan view = plan with { OneOffDays = [] };
        var days = new List<DpReviewDay>();
        foreach (Guid id in TemplatesUsed(plan))
        {
            if (plan.Templates.FirstOrDefault(template => template.Id == id) is not PlanTemplate template
                || SampleDate(view, today, id) is not DateOnly date)
            {
                continue;
            }

            days.Add(new DpReviewDay($"{template.Name} · {WhenText(plan, id)}", Lines(view, date, context)));
        }

        return days;
    }

    /// <summary>"Mon–Fri", or "week 1: Mon–Fri · week 2: Mon, Wed" with several weeks.</summary>
    public static string WhenText(DayPlan plan, Guid templateId)
    {
        var parts = new List<string>();
        for (int index = 0; index < plan.Cycle.Weeks.Count; index++)
        {
            List<DayOfWeek> days = DayPlan.MondayFirst
                .Where(day => (plan.Cycle.Weeks[index].FirstOrDefault(entry => entry.Day == day)?.TemplateId ?? DayPlan.OffDayTemplateId) == templateId)
                .ToList();
            if (days.Count == 0)
            {
                continue;
            }

            string text = DayRanges(days);
            parts.Add(plan.Cycle.Weeks.Count > 1 ? $"week {index + 1}: {text}" : text);
        }

        return string.Join(" · ", parts);
    }

    /// <summary>The 12-hour clock text of a time: "9:00 AM".</summary>
    public static string Clock(TimeOnly time)
    {
        return time.ToString("h:mm tt", CultureInfo.InvariantCulture);
    }

    /// <summary>A length as "1 h", "1 h 30 min" or "45 min".</summary>
    public static string Hours(int minutes)
    {
        return minutes < 60 ? $"{minutes} min" : minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes / 60} h {minutes % 60} min";
    }

    // The templates the cycle uses, in the order its weeks list them (Monday first); a weekday a week leaves out follows Off day.
    private static List<Guid> TemplatesUsed(DayPlan plan)
    {
        var used = new List<Guid>();
        foreach (IReadOnlyList<WeekdayTemplate> week in plan.Cycle.Weeks)
        {
            foreach (DayOfWeek day in DayPlan.MondayFirst)
            {
                Guid id = week.FirstOrDefault(entry => entry.Day == day)?.TemplateId ?? DayPlan.OffDayTemplateId;
                if (!used.Contains(id))
                {
                    used.Add(id);
                }
            }
        }

        // Workday first, so the day the guards exist for comes first.
        return used.OrderBy(id => id == DayPlan.WorkdayTemplateId ? 0 : 1).ToList();
    }

    // A date of the kind, preferably one whose next day is the same kind too (so its evening shows that kind's bedtime, not the
    // weekend's after a Friday).
    private static DateOnly? SampleDate(DayPlan view, DateOnly today, Guid templateId)
    {
        bool Is(DateOnly date) => (view.Cycle.TemplateIdFor(date) ?? DayPlan.OffDayTemplateId) == templateId;
        DateOnly? first = null;
        for (int offset = 0; offset < 7 * view.Cycle.Weeks.Count + 7; offset++)
        {
            DateOnly date = today.AddDays(offset);
            if (!Is(date))
            {
                continue;
            }

            if (Is(date.AddDays(1)))
            {
                return date;
            }

            first ??= date;
        }

        return first;
    }

    private static string DayRanges(IReadOnlyList<DayOfWeek> days)
    {
        var runs = new List<List<DayOfWeek>>();
        foreach (DayOfWeek day in days)
        {
            if (runs.Count > 0 && Position(runs[^1][^1]) + 1 == Position(day))
            {
                runs[^1].Add(day);
            }
            else
            {
                runs.Add([day]);
            }
        }

        return string.Join(", ", runs.Select(run => run.Count >= 3 ? $"{Short(run[0])}–{Short(run[^1])}" : string.Join(", ", run.Select(Short))));
    }

    private static int Position(DayOfWeek day) => ((int)day + 6) % 7;

    private static string Short(DayOfWeek day) => day.ToString()[..3];

    private static List<string> Lines(DayPlan view, DateOnly date, DpReviewContext context)
    {
        DayTemplate template = view.TemplateFor(date);
        DateTime midnight = date.ToDateTime(TimeOnly.MinValue);
        var lines = new List<(DateTime At, string Text)>();
        if (template.Mode == DayMode.Free)
        {
            lines.Add((midnight, "Free day: nothing is guarded, all day."));
        }
        else
        {
            IReadOnlyList<PlannedPart> parts = DayWorkWindows.PartsIn(view, new LocalWindow(midnight, midnight.AddDays(1)), midnight, null, context.NightFallback);
            AddPreWork(parts, lines);
            AddWork(template, parts, context, lines);
            AddLunch(view, date, lines);
        }

        AddSleep(view, date, context, lines);
        return lines.OrderBy(line => line.At).Select(line => line.Text).ToList();
    }

    private static void AddPreWork(IReadOnlyList<PlannedPart> parts, List<(DateTime At, string Text)> lines)
    {
        foreach (PlannedPart part in parts.Where(part => part.Category.Behavior == CategoryBehavior.PreWork))
        {
            lines.Add((part.Window.Start,
                $"{Range(part.Window)}: Pre-Work, a guarded warm-up before work (work tools and distractions stay minimized once you say today is a work day)."));
        }
    }

    private static void AddWork(DayTemplate template, IReadOnlyList<PlannedPart> parts, DpReviewContext context, List<(DateTime At, string Text)> lines)
    {
        List<LocalWindow> work = Merge(parts.Where(part => part.Category.Behavior == CategoryBehavior.Watch).Select(part => part.Window));
        List<LocalWindow> comms = Merge(parts.Where(part => part.Category.Behavior == CategoryBehavior.Comms).Select(part => part.Window));
        string goal = Hours(context.GoalMinutes);
        if (template.Mode == DayMode.Budget)
        {
            int budget = template.BudgetMinutes ?? 0;
            string windows = work.Count == 0 ? "wake to bedtime" : string.Join(", ", work.Select(Range));
            lines.Add((work.Count == 0 ? DateTime.MinValue : work[0].Start,
                $"{windows}: a budget of {Hours(budget)} of focus, any time in the day. Guarded until it is met, then the guard lifts."));
            return;
        }

        if (work.Count == 0 && comms.Count == 0)
        {
            lines.Add((DateTime.MinValue, "No work is planned: nothing is guarded."));
            return;
        }

        if (work.Count > 0)
        {
            string guard = context.WorkHoursGuardOn
                ? $"Guarded until you have done {goal} of focus, then it eases off for the day."
                : $"The buddy asks about new sites and counts drift; the goal is {goal} of focus.";
            lines.Add((work[0].Start, $"{string.Join(", ", work.Select(Range))}: work hours. {guard}"));
        }

        if (comms.Count > 0)
        {
            lines.Add((comms[0].Start, $"{string.Join(", ", comms.Select(Range))}: Comms windows, work time for mail and chat."));
        }

        DateTime end = work.Concat(comms).Max(window => window.End);
        if (end.Date == work.Concat(comms).Min(window => window.Start).Date)
        {
            lines.Add((end, $"After {Clock(TimeOnly.FromDateTime(end))}: stays out of your way."));
        }
    }

    private static void AddLunch(DayPlan view, DateOnly date, List<(DateTime At, string Text)> lines)
    {
        if (view.FixedLunchOn(date) is FixedLunch fixedLunch)
        {
            lines.Add((fixedLunch.Window.Start,
                $"{Clock(TimeOnly.FromDateTime(fixedLunch.Window.Start))}: lunch starts by itself and lasts until {Clock(TimeOnly.FromDateTime(fixedLunch.Window.End))}. "
                + "A focus session that runs into it asks you first: Keep, Adjust or Flexible this time."));
        }
        else if (view.FlexibleBreakOn(date) is FlexibleBreak flexible)
        {
            string hint = flexible.HintAt is DateTime at ? $" A hint comes at {Clock(TimeOnly.FromDateTime(at))} if you haven't." : string.Empty;
            lines.Add((flexible.Window.Start,
                $"{Range(flexible.Window)}: lunch window. Press Start lunch when you go ({Hours((int)flexible.Length.TotalMinutes)}).{hint}"));
        }
    }

    private static void AddSleep(DayPlan view, DateOnly date, DpReviewContext context, List<(DateTime At, string Text)> lines)
    {
        if (!context.SleepOn)
        {
            return;
        }

        DateOnly tomorrow = date.AddDays(1);
        if ((DayPlanSleep.NightBefore(view, tomorrow) ?? context.NightFallback?.Invoke(tomorrow)) is not SleepTimes night)
        {
            return;
        }

        // A bedtime before noon is after midnight, on the day after this one.
        DateTime bedtime = (night.Bedtime < new TimeOnly(12, 0) ? tomorrow : date).ToDateTime(night.Bedtime);
        string wake = night.WakeByAlarm
            ? $"Wake time comes from {context.WakeSource}."
            : $"Wake at {Clock(night.Wake)}.";
        string smart = context.SmartBedtime ? " Your phone's smart bedtime may move it earlier." : string.Empty;
        IReadOnlyList<BedtimeReminderTimes.Due> reminders = BedtimeReminderTimes.For(bedtime, context.Reminders);
        if (reminders.Count > 0)
        {
            string times = string.Join(", ", reminders.Select(due => Clock(TimeOnly.FromDateTime(due.At))));
            lines.Add((reminders[0].At, $"{times}: bedtime reminders. Bed by {Clock(night.Bedtime)}. {wake}{smart}"));
        }
        else
        {
            lines.Add((bedtime, $"{Clock(night.Bedtime)}: bedtime. {wake}{smart}"));
        }
    }

    private static string Range(LocalWindow window)
    {
        return $"{Clock(TimeOnly.FromDateTime(window.Start))}–{Clock(TimeOnly.FromDateTime(window.End))}";
    }

    // Windows that touch become one.
    private static List<LocalWindow> Merge(IEnumerable<LocalWindow> windows)
    {
        var merged = new List<LocalWindow>();
        foreach (LocalWindow window in windows.OrderBy(window => window.Start))
        {
            if (merged.Count > 0 && window.Start <= merged[^1].End)
            {
                merged[^1] = new LocalWindow(merged[^1].Start, window.End > merged[^1].End ? window.End : merged[^1].End);
            }
            else
            {
                merged.Add(window);
            }
        }

        return merged;
    }
}
