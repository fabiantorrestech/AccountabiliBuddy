using System.Text.Json;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// The Setup DP Assistant's two plan operations: <see cref="Prefill"/> reads the answers a plan already gives (else the
/// defaults), and <see cref="Apply"/> writes the answers back into a plan. Only the "Workday" type's work time and lunch,
/// the nights of "Workday" and "Off day", and the cycle's weeks are touched; every other part of the plan stays.
/// </summary>
public static class DpAssistantPlanBuilder
{
    /// <summary>
    /// The answers <paramref name="plan"/> already gives. The starting plan's own timeline counts as "not set up yet" (it
    /// becomes the default work window); a timeline the user changed is offered to keep.
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
        DpLunchKind lunchKind = lunch is { AutoStart: true } ? DpLunchKind.Fixed : DpLunchKind.Flexible;
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

    /// <summary>The plan with <paramref name="choices"/> written into it.</summary>
    public static DayPlan Apply(DayPlan plan, DpAssistantPlanChoices choices)
    {
        DayTemplate workday = WithLunch(plan, WithWorkTime(plan, plan.Workday, choices), choices);
        DayPlan next = plan.WithWorkday(workday);
        if (choices.WorkdayNight is SleepTimes workdayNight)
        {
            next = next.WithWorkday(DayPlanSleep.WithNight(next.Workday, next, workdayNight));
        }

        if (choices.OtherNight is SleepTimes otherNight)
        {
            next = next.WithOtherDay(DayPlanSleep.WithNight(next.OtherDay, next, otherNight));
        }

        return next with { Cycle = new PlanCycle(choices.Weeks, choices.StartsOn) };
    }

    /// <summary>True when the Workday type has a timeline of its own with work in it (so keeping it is an option).</summary>
    public static bool HasTimeline(DayPlan plan, DayTemplate workday)
    {
        return workday.Mode == DayMode.Schedule && workday.WorkWindow is null
            && workday.Parts.Any(part => plan.Category(part.CategoryId)?.Behavior.IsWork() == true);
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
    // label), or is added when the day has none and the plan has a Rest category to put it in.
    private static DayTemplate WithLunch(DayPlan plan, DayTemplate workday, DpAssistantPlanChoices choices)
    {
        DayCategory? category = LunchCategory(plan);
        if (category is null)
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
        parts.Add(lunch);
        return workday with { Parts = parts };
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
}
