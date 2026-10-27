using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>Checks a day plan before it's saved: parts don't overlap, last at least 15 minutes and use a known category.</summary>
public static class DayPlanValidator
{
    public static TimeSpan ShortestPart { get; } = TimeSpan.FromMinutes(15);

    /// <returns>Null when fine; otherwise what's wrong, e.g. "Work 12:00–13:00 overlaps Lunch 12:30–13:30."</returns>
    public static string? Validate(DayTemplate template, IReadOnlyList<DayCategory> categories)
    {
        if (!Enum.IsDefined(template.Mode))
        {
            return "A day has a mode this version doesn't know.";
        }

        if (template.Mode == DayMode.Budget)
        {
            if (template.BudgetMinutes is not (>= 15 and <= 24 * 60))
            {
                return "A budget day needs a budget between 15 minutes and 24 hours.";
            }

            // A budget day's parts are only the non-productive windows (and Pre-Work).
            if (template.Parts.Any(part => categories.FirstOrDefault(category => category.Id == part.CategoryId)?.Behavior.IsWork() == true))
            {
                return "A budget day's parts are only rest, sleep and pre-work windows.";
            }
        }

        if (template.WorkWindow is WorkWindow window && window.End != TimeOnly.MinValue && window.End <= window.Start)
        {
            return "The work window must end after it starts.";
        }

        if (template.WorkWindow is not null && categories.All(category => category.Behavior != CategoryBehavior.Watch))
        {
            return "A work window needs a Work category.";
        }

        // A work window counts as the day's work part (when it has none of its own), so it is checked like one.
        template = template.WithWorkWindowPart(categories);
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

            // A fixed lunch period (schema v13): a Rest part with fixed hours that doesn't cross midnight.
            if (part.AutoStart
                && (categories.First(category => category.Id == part.CategoryId).Behavior != CategoryBehavior.Rest || part.IsFlexible || part.CrossesMidnight))
            {
                return $"{Describe(part, categories)} can't start by itself: only a rest part with fixed hours, within one day, can.";
            }
        }

        for (int i = 0; i < template.Parts.Count; i++)
        {
            for (int j = i + 1; j < template.Parts.Count; j++)
            {
                if (Overlap(template.Parts[i], template.Parts[j])
                    && !template.Parts[i].IsFlexible && !template.Parts[j].IsFlexible
                    && !DayTemplateEditing.MayOverlap(template.Parts[i].CategoryId, template.Parts[j].CategoryId, categories))
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

        if (plan.Categories.Any(category => string.IsNullOrWhiteSpace(category.Name)))
        {
            return "Every category needs a name.";
        }

        if (plan.Categories.GroupBy(category => category.Name.Trim(), StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            return "Two categories have the same name.";
        }

        if (plan.Categories.GroupBy(category => category.Id).Any(group => group.Count() > 1))
        {
            return "Two categories have the same id.";
        }

        if (plan.Categories.FirstOrDefault(category => !IsColor(category.Color)) is DayCategory badColor)
        {
            return $"{badColor.Name} has a colour that isn't #RRGGBB.";
        }

        if (plan.OneOffDays.GroupBy(day => day.Date).Any(group => group.Count() > 1))
        {
            return "Two one-off days are on the same date.";
        }

        if (Templates(plan) is string templateProblem)
        {
            return templateProblem;
        }

        if (Week(plan) is string weekProblem)
        {
            return weekProblem;
        }

        return plan.OneOffDays.Select(day => Validate(day.Template, plan.Categories) is string problem ? $"{day.Date:ddd d MMM}: {problem}" : null)
            .FirstOrDefault(problem => problem is not null);
    }

    private static string? Templates(DayPlan plan)
    {
        if (plan.Templates.Any(template => string.IsNullOrWhiteSpace(template.Name)))
        {
            return "Every day type needs a name.";
        }

        if (plan.Templates.GroupBy(template => template.Name.Trim(), StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            return "Two day types have the same name.";
        }

        if (plan.Templates.GroupBy(template => template.Id).Any(group => group.Count() > 1))
        {
            return "Two day types have the same id.";
        }

        if (plan.Templates.All(template => template.Id != DayPlan.OffDayTemplateId))
        {
            return "Keep the Off day type: it is what any day not mapped to a type follows.";
        }

        return plan.Templates.Select(template => Validate(template.Template, plan.Categories) is string problem ? $"{template.Name}: {problem}" : null)
            .FirstOrDefault(problem => problem is not null);
    }

    private static string? Week(DayPlan plan)
    {
        if (plan.Cycle.Weeks.Count is < 1 or > PlanCycle.MaxWeeks)
        {
            return $"The repeating weeks need between 1 and {PlanCycle.MaxWeeks} weeks.";
        }

        for (int index = 0; index < plan.Cycle.Weeks.Count; index++)
        {
            IReadOnlyList<WeekdayTemplate> week = plan.Cycle.Weeks[index];
            if (week.GroupBy(entry => entry.Day).Any(group => group.Count() > 1))
            {
                return $"Week {index + 1} lists a weekday twice.";
            }

            if (week.Any(entry => plan.Templates.All(template => template.Id != entry.TemplateId)))
            {
                return $"Week {index + 1} uses a day type that no longer exists.";
            }
        }

        if (plan.Cycle.StartsOn.DayOfWeek != DayOfWeek.Monday)
        {
            return "The repeating weeks need a Monday for week 1 to start on.";
        }

        return null;
    }

    private static bool IsColor(string? color)
    {
        return color is { Length: 7 } && color[0] == '#' && color.Skip(1).All(Uri.IsHexDigit);
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
