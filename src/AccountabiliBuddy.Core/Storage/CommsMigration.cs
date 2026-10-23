using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;

namespace AccountabiliBuddy.Core.Storage;

/// <summary>
/// Profile schema v5 to v6 (comms windows): every existing plan gains only the Comms category, so nothing about the
/// user's days changes until they add comms parts themselves (only a fresh install's starting plan has two windows).
/// </summary>
public static class CommsMigration
{
    private static readonly Guid Work = Guid.Parse("8b1f0c52-0001-4c6e-9a51-3f2d7c000001");
    private static readonly Guid Lunch = Guid.Parse("8b1f0c52-0003-4c6e-9a51-3f2d7c000003");

    /// <summary>The starting plan as v5 had it (before comms windows).</summary>
    public static DayPlan V5Default { get; } = DayPlan.Default with
    {
        Categories = DayPlan.Default.Categories
            .Where(category => category.Id != DayPlan.CommsCategory.Id && category.Id != DayPlan.PreWorkCategory.Id)
            .ToList(),
        Workday = new DayTemplate(
        [
            new PlanPart(Guid.Parse("5c7a2e10-0001-4b3d-8f6e-1a9c40000001"), Work, new TimeOnly(9, 0), new TimeOnly(12, 0)),
            new PlanPart(Guid.Parse("5c7a2e10-0002-4b3d-8f6e-1a9c40000002"), Lunch, new TimeOnly(12, 0), new TimeOnly(13, 0)),
            new PlanPart(Guid.Parse("5c7a2e10-0003-4b3d-8f6e-1a9c40000003"), Work, new TimeOnly(13, 0), new TimeOnly(17, 0)),
            .. DayPlan.Default.Workday.Parts.Where(part => part.Start >= new TimeOnly(18, 0)),
        ]),
    };

    public static DayPlan Upgrade(DayPlan plan)
    {
        return plan.Categories.Any(category => category.Behavior == CategoryBehavior.Comms)
            ? plan
            : plan with { Categories = [.. plan.Categories, DayPlan.CommsCategory] };
    }
}
