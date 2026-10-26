using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Sleep mode's bedtime and wake time live in the day plan's Sleep parts: the Workday template's Sleep is the night
/// before a workday ("weeknights"), the Off-Days template's the night before any other day ("weekends").
/// </summary>
public static class DayPlanSleep
{
    /// <returns>The template's night (its Sleep part from the evening before, or one starting in the early hours) as
    /// bedtime and wake time; null when it has none. A nap later in the day isn't the night.</returns>
    public static SleepTimes? SleepOf(DayTemplate template, DayPlan plan)
    {
        PlanPart? sleep = SleepPart(template, plan);
        return sleep is null ? null : new SleepTimes(sleep.Start, sleep.End, sleep.WakeByAlarm);
    }

    /// <summary>The night before <paramref name="day"/> as the plan has it for that day (one-off days included).</summary>
    public static SleepTimes? NightBefore(DayPlan plan, DateOnly day)
    {
        return SleepOf(plan.TemplateFor(day), plan);
    }

    /// <summary>Sets each template's Sleep part to these times (keeping its id), or adds one when it has none.</summary>
    public static DayPlan WithSleep(DayPlan plan, SleepTimes workdayNight, SleepTimes otherNight)
    {
        DayCategory? category = plan.Categories.FirstOrDefault(candidate => candidate.Behavior == CategoryBehavior.Sleep);
        if (category is null)
        {
            return plan;
        }

        return plan with
        {
            Workday = WithSleep(plan.Workday, plan, category, workdayNight),
            OtherDay = WithSleep(plan.OtherDay, plan, category, otherNight),
        };
    }

    /// <summary>
    /// One template with its night set to <paramref name="times"/> (wake by alarm included), e.g. a one-off day from
    /// "Plan a day"; unchanged when the plan has no Sleep category.
    /// </summary>
    public static DayTemplate WithNight(DayTemplate template, DayPlan plan, SleepTimes times)
    {
        DayCategory? category = plan.Categories.FirstOrDefault(candidate => candidate.Behavior == CategoryBehavior.Sleep);
        if (category is null)
        {
            return template;
        }

        DayTemplate set = WithSleep(template, plan, category, times);
        return set with { Parts = set.Parts.Select(part => part.Id == SleepPart(set, plan)?.Id ? part with { WakeByAlarm = times.WakeByAlarm } : part).ToList() };
    }

    /// <summary>One template without its night ("Plan a day": leave sleep blank); a nap later in the day stays.</summary>
    public static DayTemplate WithoutNight(DayTemplate template, DayPlan plan)
    {
        PlanPart? night = SleepPart(template, plan);
        return night is null ? template : template with { Parts = template.Parts.Where(part => part.Id != night.Id).ToList() };
    }

    private static DayTemplate WithSleep(DayTemplate template, DayPlan plan, DayCategory category, SleepTimes times)
    {
        PlanPart? existing = SleepPart(template, plan);
        PlanPart sleep = existing is null
            ? new PlanPart(Guid.NewGuid(), category.Id, times.Bedtime, times.Wake)
            : existing with { Start = times.Bedtime, End = times.Wake };
        List<PlanPart> parts = template.Parts.Select(part => part == existing ? sleep : part).ToList();
        if (existing is null)
        {
            parts.Add(sleep);
        }

        return template with { Parts = parts };
    }

    // The night: a Sleep part from the evening before first, else one starting before noon (after midnight).
    private static PlanPart? SleepPart(DayTemplate template, DayPlan plan)
    {
        List<PlanPart> sleeps = template.Parts.Where(part => plan.Category(part.CategoryId)?.Behavior == CategoryBehavior.Sleep).ToList();
        return sleeps.FirstOrDefault(part => part.CrossesMidnight)
               ?? sleeps.Where(part => part.Start < new TimeOnly(12, 0)).OrderBy(part => part.Start).FirstOrDefault();
    }
}
