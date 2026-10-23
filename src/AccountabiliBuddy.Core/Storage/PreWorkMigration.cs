using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Storage;

/// <summary>Schema v7 (ADR 31): every plan gets the Pre-Work category, with no parts.</summary>
public static class PreWorkMigration
{
    public static DayPlan Upgrade(DayPlan plan)
    {
        return plan.Categories.Any(category => category.Behavior == CategoryBehavior.PreWork)
            ? plan
            : plan with { Categories = [.. plan.Categories, DayPlan.PreWorkCategory] };
    }
}
