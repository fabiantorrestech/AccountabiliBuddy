using System.Globalization;
using System.Text.Json;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// The Setup DP Assistant's plan operations: <see cref="Prefill"/> reads the answers a plan already gives (else the
/// defaults), <see cref="Apply"/> writes answers back, and <see cref="TimelineChanges"/> names the timed parts a change
/// removes or cuts. <see cref="Apply"/> rewrites a section (work time, lunch, a night, the weeks) only when that section's
/// answers differ from the <see cref="Prefill"/> baseline, so clicking through the prefilled assistant leaves the plan
/// exactly as it was. Only the "Workday" type's work time and lunch, the nights of "Workday" and "Off day", and the
/// cycle's weeks are ever touched.
/// </summary>
public static class DpAssistantPlanBuilder
{
    /// <summary>
    /// The answers <paramref name="plan"/> already gives. A plain lunch part is kept as it is; the starting plan's own
    /// timeline counts as "not set up yet" (the default work window shows, but nothing changes unless the user edits it);
    /// a timeline the user changed is offered to keep.
    /// </summary>
    /// <param name="fallbackWorkdayNight">The night before a workday when the plan has none (the sleep settings' weeknight).</param>
    /// <param name="fallbackOtherNight">The night before another day when the plan has none (the sleep settings' weekend night).</param>
    public static DpAssistantPlanChoices Prefill(DayPlan plan, SleepTimes fallbackWorkdayNight, SleepTimes fallbackOtherNight)
    {
        DayTemplate workday = plan.Workday;
        DpWorkTime workTime = DpWorkTime.Window;
        TimeOnly windowStart = DpAssistantPlanChoices.DefaultWindow.Start;
        TimeOnly windowEnd = DpAssistantPlanChoices.DefaultWindow.End;
        int budget = workday.BudgetMinutes is int minutes ? minutes : DpAssistantPlanChoices.DefaultBudgetMinutes;
        if (workday.Mode == DayMode.Budget)
        {
            workTime = DpWorkTime.Budget;
        }
        else if (workday.WorkWindow is WorkWindow window)
        {
            windowStart = window.Start;
            windowEnd = window.End;
        }
        else if (HasTimeline(plan, workday) && !IsStarterTimeline(workday))
        {
            workTime = DpWorkTime.KeepTimeline;
            List<PlanPart> work = workday.Parts.Where(part => plan.Category(part.CategoryId)?.Behavior.IsWork() == true && !part.CrossesMidnight).ToList();
            if (work.Count > 0)
            {
                windowStart = work.Min(part => part.Start);
                windowEnd = work.OrderBy(part => part.EndMinutes).Last().End;
            }
        }

        PlanPart? lunch = LunchPart(plan, workday);
        DpLunchKind lunchKind = lunch switch
        {
            { AutoStart: true } => DpLunchKind.Fixed,
            { IsFlexible: false } => DpLunchKind.Keep,
            _ => DpLunchKind.Flexible,
        };
        (TimeOnly Start, TimeOnly End, int Minutes, TimeOnly Hint) flexible = DpAssistantPlanChoices.DefaultLunchWindow;
        if (lunch is { IsFlexible: true })
        {
            flexible = (lunch.Start, lunch.End, lunch.FlexMinutes!.Value, lunch.HintAt ?? flexible.Hint);
        }

        (TimeOnly Start, TimeOnly End) fixedLunch = DpAssistantPlanChoices.DefaultFixedLunch;
        if (lunch is { IsFlexible: false, CrossesMidnight: false })
        {
            fixedLunch = (lunch.Start, lunch.End);
        }

        return new DpAssistantPlanChoices(
            workTime, windowStart, windowEnd, budget,
            lunchKind, flexible.Start, flexible.End, flexible.Minutes, flexible.Hint, fixedLunch.Start, fixedLunch.End,
            DayPlanSleep.TypicalNight(plan, workday: true) ?? fallbackWorkdayNight,
            DayPlanSleep.TypicalNight(plan, workday: false) ?? fallbackOtherNight,
            plan.Cycle.Weeks.Select(week => (IReadOnlyList<WeekdayTemplate>)week.ToList()).ToList(),
            plan.Cycle.StartsOn);
    }

    /// <summary>
    /// The plan with the sections of <paramref name="choices"/> that differ from <paramref name="baseline"/> (the prefill) written
    /// into it; every other section stays byte for byte as stored. A night that is null is skipped sleep: nothing is written.
    /// </summary>
    public static DayPlan Apply(DayPlan plan, DpAssistantPlanChoices choices, DpAssistantPlanChoices baseline)
    {
        DayPlan next = plan;
        bool workChanged = WorkTimeChanged(choices, baseline);
        bool lunchChanged = LunchChanged(choices, baseline);
        if (workChanged || lunchChanged)
        {
            DayTemplate workday = plan.Workday;
            if (workChanged)
            {
                workday = WithWorkTime(plan, workday, choices);
            }

            if (lunchChanged)
            {
                workday = WithLunch(plan, workday, choices);
            }

            next = next.WithWorkday(workday);
        }

        if (choices.WorkdayNight is SleepTimes workdayNight && workdayNight != baseline.WorkdayNight)
        {
            next = next.WithWorkday(DayPlanSleep.WithNight(next.Workday, next, workdayNight));
        }

        if (choices.OtherNight is SleepTimes otherNight && otherNight != baseline.OtherNight)
        {
            next = next.WithOtherDay(DayPlanSleep.WithNight(next.OtherDay, next, otherNight));
        }

        return SameWeeks(choices, baseline) ? next : next with { Cycle = new PlanCycle(choices.Weeks, choices.StartsOn) };
    }

    /// <summary>
    /// What a change does to the timed work parts (work and Comms) of the Workday type, one line each, named with their times:
    /// "Extra Work 6:00–8:00 PM is removed", "Work 9:00 AM–5:00 PM is cut to 9:00 AM–12:00 PM and 1:00–5:00 PM". Empty when none.
    /// </summary>
    public static IReadOnlyList<string> TimelineChanges(DayPlan before, DayPlan after)
    {
        var lines = new List<string>();
        IReadOnlyList<PlanPart> kept = after.Workday.Parts;
        HashSet<Guid> oldIds = before.Workday.Parts.Select(part => part.Id).ToHashSet();
        foreach (PlanPart part in before.Workday.Parts.Where(part => before.Category(part.CategoryId)?.Behavior.IsWork() == true))
        {
            string name = $"{part.Label ?? before.Category(part.CategoryId)!.Name} {Range(part.Start, part.End)}";
            List<PlanPart> pieces = kept
                .Where(other => other.Id == part.Id
                    || (!oldIds.Contains(other.Id) && other.CategoryId == part.CategoryId && other.Label == part.Label
                        && other.Start >= part.Start && other.EndMinutes <= part.EndMinutes))
                .OrderBy(other => other.Start)
                .ToList();
            if (pieces.Count == 0)
            {
                lines.Add($"{name} is removed.");
            }
            else if (pieces.Count != 1 || pieces[0].Start != part.Start || pieces[0].End != part.End)
            {
                lines.Add($"{name} is cut to {string.Join(" and ", pieces.Select(piece => Range(piece.Start, piece.End)))}.");
            }
        }

        return lines;
    }

    /// <summary>True when the Workday type has a timeline of its own with work in it (so keeping it is an option).</summary>
    public static bool HasTimeline(DayPlan plan, DayTemplate workday)
    {
        return workday.Mode == DayMode.Schedule && workday.WorkWindow is null
            && workday.Parts.Any(part => plan.Category(part.CategoryId)?.Behavior.IsWork() == true);
    }

    private static bool WorkTimeChanged(DpAssistantPlanChoices choices, DpAssistantPlanChoices baseline)
    {
        return choices.WorkTime != baseline.WorkTime
            || (choices.WorkTime == DpWorkTime.Window && (choices.WindowStart != baseline.WindowStart || choices.WindowEnd != baseline.WindowEnd))
            || (choices.WorkTime == DpWorkTime.Budget && choices.BudgetMinutes != baseline.BudgetMinutes);
    }

    private static bool LunchChanged(DpAssistantPlanChoices choices, DpAssistantPlanChoices baseline)
    {
        return choices.Lunch != baseline.Lunch
            || (choices.Lunch == DpLunchKind.Flexible
                && (choices.LunchWindowStart != baseline.LunchWindowStart || choices.LunchWindowEnd != baseline.LunchWindowEnd
                    || choices.LunchMinutes != baseline.LunchMinutes || choices.LunchHint != baseline.LunchHint))
            || (choices.Lunch == DpLunchKind.Fixed && (choices.FixedLunchStart != baseline.FixedLunchStart || choices.FixedLunchEnd != baseline.FixedLunchEnd));
    }

    // The weeks as plain data, a weekday a week leaves out following Off day (what the cycle means), so a grid that spells every
    // weekday out equals a plan that left some out.
    private static bool SameWeeks(DpAssistantPlanChoices choices, DpAssistantPlanChoices baseline)
    {
        return choices.StartsOn == baseline.StartsOn && NormalizedWeeks(choices.Weeks) == NormalizedWeeks(baseline.Weeks);
    }

    private static string NormalizedWeeks(IReadOnlyList<IReadOnlyList<WeekdayTemplate>> weeks)
    {
        return JsonSerializer.Serialize(weeks.Select(week => DayPlan.MondayFirst
            .Select(day => week.FirstOrDefault(entry => entry.Day == day)?.TemplateId ?? DayPlan.OffDayTemplateId)
            .ToList()).ToList());
    }

    /// <summary>True when the Workday type's parts are still the starting plan's: nothing the user shaped.</summary>
    private static bool IsStarterTimeline(DayTemplate workday)
    {
        return Normalized(workday.Parts) == Normalized(DayPlan.Default.Workday.Parts);
    }

    private static string Normalized(IReadOnlyList<PlanPart> parts)
    {
        return JsonSerializer.Serialize(parts.OrderBy(part => part.Id).ToList());
    }

    // A window or a budget replaces the day's work and Comms parts (a Comms part would stop the window standing in for the
    // work part); everything else on the day (lunch, Pre-Work, sleep, rest) stays.
    private static DayTemplate WithWorkTime(DayPlan plan, DayTemplate workday, DpAssistantPlanChoices choices)
    {
        List<PlanPart> rest = workday.Parts.Where(part => plan.Category(part.CategoryId)?.Behavior.IsWork() != true).ToList();
        return choices.WorkTime switch
        {
            DpWorkTime.Window => workday with
            {
                Mode = DayMode.Schedule,
                Parts = rest,
                WorkWindow = new WorkWindow(choices.WindowStart, choices.WindowEnd),
                BudgetMinutes = null,
            },
            DpWorkTime.Budget => workday with
            {
                Mode = DayMode.Budget,
                Parts = rest,
                WorkWindow = null,
                BudgetMinutes = choices.BudgetMinutes,
            },
            _ => workday,
        };
    }

    // The lunch part of the Workday type becomes a lunch window or a fixed period that starts by itself (keeping its id and
    // label), or is added when the day has none and the plan has a Rest category to put it in. "Keep" leaves it. A fixed
    // lunch cuts the work parts the day still has around it, so they don't overlap it.
    private static DayTemplate WithLunch(DayPlan plan, DayTemplate workday, DpAssistantPlanChoices choices)
    {
        DayCategory? category = LunchCategory(plan);
        if (category is null || choices.Lunch == DpLunchKind.Keep)
        {
            return workday;
        }

        PlanPart? existing = LunchPart(plan, workday);
        Guid id = existing?.Id ?? Guid.NewGuid();
        PlanPart lunch = choices.Lunch == DpLunchKind.Flexible
            ? new PlanPart(id, category.Id, choices.LunchWindowStart, choices.LunchWindowEnd, existing?.Label,
                FlexMinutes: Math.Clamp(choices.LunchMinutes, 15, 240), HintAt: choices.LunchHint)
            : new PlanPart(id, category.Id, choices.FixedLunchStart, choices.FixedLunchEnd, existing?.Label, AutoStart: true);
        List<PlanPart> parts = workday.Parts.Where(part => part.Id != existing?.Id).ToList();
        if (choices.Lunch == DpLunchKind.Fixed)
        {
            parts = CutAround(plan, parts, lunch);
        }

        parts.Add(lunch);
        return workday with { Parts = parts };
    }

    // The work and Comms parts that overlap the fixed lunch, cut around it with the work window's own piece logic
    // (DayTemplate.WindowPieces: pieces under 15 minutes are left out). The first piece keeps the part's id.
    private static List<PlanPart> CutAround(DayPlan plan, List<PlanPart> parts, PlanPart lunch)
    {
        var holes = new DayTemplate([lunch]);
        var result = new List<PlanPart>();
        foreach (PlanPart part in parts)
        {
            if (plan.Category(part.CategoryId) is not DayCategory category || !category.Behavior.IsWork() || part.CrossesMidnight
                || part.EndMinutes <= (int)lunch.Start.ToTimeSpan().TotalMinutes || (int)part.Start.ToTimeSpan().TotalMinutes >= lunch.EndMinutes)
            {
                result.Add(part);
                continue;
            }

            List<PlanPart> pieces = holes.WindowPieces(new WorkWindow(part.Start, part.End), category);
            for (int index = 0; index < pieces.Count; index++)
            {
                result.Add(part with { Id = index == 0 ? part.Id : Guid.NewGuid(), Start = pieces[index].Start, End = pieces[index].End });
            }
        }

        return result;
    }

    // A Rest category named Lunch, else the first Rest one; null when the plan has none.
    private static DayCategory? LunchCategory(DayPlan plan)
    {
        List<DayCategory> rest = plan.Categories.Where(category => category.Behavior == CategoryBehavior.Rest).ToList();
        return rest.FirstOrDefault(category => IsLunchName(category.Name)) ?? rest.FirstOrDefault();
    }

    // The day's lunch: a flexible break, else a part that starts by itself, else a Rest part in the Lunch category.
    private static PlanPart? LunchPart(DayPlan plan, DayTemplate workday)
    {
        List<PlanPart> rest = workday.Parts.Where(part => plan.Category(part.CategoryId)?.Behavior == CategoryBehavior.Rest).ToList();
        return rest.FirstOrDefault(part => part.IsFlexible)
            ?? rest.FirstOrDefault(part => part.AutoStart)
            ?? rest.FirstOrDefault(part => plan.Category(part.CategoryId) is DayCategory category && IsLunchName(category.Name));
    }

    private static bool IsLunchName(string name)
    {
        return string.Equals(name.Trim(), "Lunch", StringComparison.OrdinalIgnoreCase);
    }

    // "6:00–8:00 PM" when both ends share AM/PM, else "11:30 AM–1:00 PM" (12-hour in any culture).
    private static string Range(TimeOnly start, TimeOnly end)
    {
        string from = start.ToString("h:mm tt", CultureInfo.InvariantCulture);
        string to = end.ToString("h:mm tt", CultureInfo.InvariantCulture);
        return start.Hour / 12 == (end == TimeOnly.MinValue ? 2 : end.Hour / 12) ? $"{start.ToString("h:mm", CultureInfo.InvariantCulture)}–{to}" : $"{from}–{to}";
    }
}
