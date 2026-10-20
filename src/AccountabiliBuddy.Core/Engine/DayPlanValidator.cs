using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>Checks a day plan before it's saved: parts don't overlap, last at least 15 minutes and use a known category.</summary>
public static class DayPlanValidator
{
    public static TimeSpan ShortestPart { get; } = TimeSpan.FromMinutes(15);

    /// <returns>Null when fine; otherwise what's wrong, e.g. "Work 12:00–13:00 overlaps Lunch 12:30–13:30."</returns>
    public static string? Validate(DayTemplate template, IReadOnlyList<DayCategory> categories)
    {
        foreach (PlanPart part in template.Parts)
        {
            if (categories.All(category => category.Id != part.CategoryId))
            {
                return "A part uses a category that no longer exists.";
            }

            if (Length(part) < ShortestPart)
            {
                return $"{Describe(part, categories)} is shorter than 15 minutes.";
            }
        }

        for (int i = 0; i < template.Parts.Count; i++)
        {
            for (int j = i + 1; j < template.Parts.Count; j++)
            {
                if (Overlap(template.Parts[i], template.Parts[j]))
                {
                    return $"{Describe(template.Parts[i], categories)} overlaps {Describe(template.Parts[j], categories)}.";
                }
            }
        }

        return null;
    }

    /// <returns>Null when the whole plan is fine; otherwise the first problem found.</returns>
    public static string? Validate(DayPlan plan)
    {
        if (plan.Categories.Count == 0)
        {
            return "Keep at least one category.";
        }

        if (plan.Categories.GroupBy(category => category.Name.Trim(), StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            return "Two categories have the same name.";
        }

        if (plan.OneOffDays.GroupBy(day => day.Date).Any(group => group.Count() > 1))
        {
            return "Two one-off days are on the same date.";
        }

        return Validate(plan.Workday, plan.Categories) is string workday ? $"Workdays: {workday}"
            : Validate(plan.OtherDay, plan.Categories) is string other ? $"Other days: {other}"
            : plan.OneOffDays.Select(day => Validate(day.Template, plan.Categories) is string problem ? $"{day.Date:ddd d MMM}: {problem}" : null)
                .FirstOrDefault(problem => problem is not null);
    }

    private static TimeSpan Length(PlanPart part)
    {
        TimeSpan length = TimeSpan.FromMinutes(part.EndMinutes) - part.Start.ToTimeSpan();
        return part.CrossesMidnight ? length + TimeSpan.FromDays(1) : length;
    }

    private static bool Overlap(PlanPart a, PlanPart b)
    {
        return DayTemplateEditing.Ranges(a).Any(x => DayTemplateEditing.Ranges(b).Any(y => x.Start < y.End && y.Start < x.End));
    }

    private static string Describe(PlanPart part, IReadOnlyList<DayCategory> categories)
    {
        string name = part.Label ?? categories.FirstOrDefault(category => category.Id == part.CategoryId)?.Name ?? "A part";
        return $"{name} {part.Start:HH\\:mm}–{part.End:HH\\:mm}";
    }
}
