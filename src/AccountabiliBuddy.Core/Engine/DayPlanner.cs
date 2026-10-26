using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Turns the "Plan a day" walkthrough's answers into one-off days (ADR 37): each date gets the Workdays or Off-Days
/// template with the night and block rules applied. The templates themselves never change.
/// </summary>
public static class DayPlanner
{
    /// <summary>The template the answers make (the same for every date).</summary>
    public static DayTemplate TemplateFor(DayPlan plan, DayPlanRequest request)
    {
        DayTemplate template = request.OffDay ? plan.OtherDay : plan.Workday;
        if (request.NoSleep)
        {
            template = DayPlanSleep.WithoutNight(template, plan);
        }
        else if (request.Sleep is SleepTimes sleep)
        {
            template = DayPlanSleep.WithNight(template, plan, sleep);
        }

        return request.Blocks is BlockRules blocks ? template with { Blocks = blocks } : template;
    }

    /// <summary>The dates that already have a one-off day (the walkthrough asks before replacing them).</summary>
    public static IReadOnlyList<DateOnly> Clashes(DayPlan plan, IReadOnlyList<DateOnly> dates)
    {
        return dates.Where(date => plan.OneOffDays.Any(day => day.Date == date)).Order().ToList();
    }

    /// <summary>The plan with a one-off day for each date (replacing any there), in date order.</summary>
    public static DayPlan Apply(DayPlan plan, DayPlanRequest request)
    {
        DayTemplate template = TemplateFor(plan, request);
        HashSet<DateOnly> dates = request.Dates.ToHashSet();
        string note = string.IsNullOrWhiteSpace(request.Note) ? (request.OffDay ? "Off-day" : "Adjusted workday") : request.Note.Trim();
        IEnumerable<OneOffDay> added = dates.Select(date =>
            new OneOffDay(plan.OneOffDays.FirstOrDefault(day => day.Date == date)?.Id ?? Guid.NewGuid(), date, template, note));
        return plan with
        {
            OneOffDays = plan.OneOffDays.Where(day => !dates.Contains(day.Date)).Concat(added).OrderBy(day => day.Date).ToList(),
        };
    }

    /// <summary>
    /// Lift window defaults: from the start of the day until the re-enable time (all day when that's midnight), Allow.
    /// </summary>
    public static BlockLift DefaultLift(IReadOnlyList<BlockRef> blocks, TimeOnly reenableAt)
    {
        return new BlockLift(TimeOnly.MinValue, reenableAt, BlockLift.Allow, blocks);
    }
}
