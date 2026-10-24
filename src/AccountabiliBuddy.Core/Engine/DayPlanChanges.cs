using System.Globalization;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// What changed between two day plans, as short lines for a clash prompt (ADR 19, 22), e.g.
/// "Workdays: Work 9:00 AM–12:00 PM → 8:30 AM–12:00 PM" or "Friday is no longer a workday". The PC's counterpart of the
/// phone's DayPlanChanges, with the same wording; times, dates and day names in the current culture.
/// </summary>
public static class DayPlanChanges
{
    public static IReadOnlyList<string> Describe(DayPlan basePlan, DayPlan edited)
    {
        var lines = new List<string>();
        Categories(basePlan, edited, lines);
        Workdays(basePlan, edited, lines);
        Template("Workdays", basePlan.Workday, edited.Workday, basePlan, edited, lines);
        Template("Off-Days", basePlan.OtherDay, edited.OtherDay, basePlan, edited, lines);
        OneOffDays(basePlan, edited, lines);
        return lines.Count > 0 ? lines : new[] { "No changes" };
    }

    private static void Categories(DayPlan basePlan, DayPlan edited, List<string> lines)
    {
        foreach (DayCategory category in edited.Categories)
        {
            if (basePlan.Category(category.Id) is not DayCategory before)
            {
                lines.Add($"New category: {category.Name}");
                continue;
            }

            // Each change on its own line: a rename and a recolour are two changes.
            if (before.Name != category.Name)
            {
                lines.Add($"Category {before.Name} renamed to {category.Name}");
            }

            if (!string.Equals(before.Color, category.Color, StringComparison.OrdinalIgnoreCase))
            {
                lines.Add($"{category.Name}: new colour");
            }

            if (before.Behavior != category.Behavior)
            {
                lines.Add($"{category.Name} now acts as {category.Behavior}");
            }
        }

        foreach (DayCategory removed in basePlan.Categories.Where(category => edited.Category(category.Id) is null))
        {
            lines.Add($"Removed category: {removed.Name}");
        }
    }

    private static void Workdays(DayPlan basePlan, DayPlan edited, List<string> lines)
    {
        foreach (DayOfWeek day in edited.Workdays.Except(basePlan.Workdays).OrderBy(MondayFirst))
        {
            lines.Add($"{DayName(day)} is now a workday");
        }

        foreach (DayOfWeek day in basePlan.Workdays.Except(edited.Workdays).OrderBy(MondayFirst))
        {
            lines.Add($"{DayName(day)} is no longer a workday");
        }
    }

    private static void Template(string title, DayTemplate before, DayTemplate after, DayPlan basePlan, DayPlan edited, List<string> lines)
    {
        foreach (PlanPart part in after.Parts)
        {
            if (before.Parts.FirstOrDefault(old => old.Id == part.Id) is not PlanPart old)
            {
                lines.Add($"{title}: added {Name(part, edited)} {Times(part)}");
                continue;
            }

            if (old.Start != part.Start || old.End != part.End)
            {
                lines.Add($"{title}: {Name(part, edited)} {Times(old)} → {Times(part)}");
            }

            if (old.Label != part.Label)
            {
                lines.Add($"{title}: {Name(old, basePlan)} renamed to {Name(part, edited)}");
            }

            // A renamed category shows once under categories, not again for every part using it.
            if (old.CategoryId != part.CategoryId)
            {
                lines.Add($"{title}: {Name(part, edited)} is now {edited.Category(part.CategoryId)?.Name ?? "another category"}");
            }
        }

        foreach (PlanPart removed in before.Parts.Where(old => after.Parts.All(part => part.Id != old.Id)))
        {
            lines.Add($"{title}: removed {Name(removed, basePlan)} {Times(removed)}");
        }
    }

    private static void OneOffDays(DayPlan basePlan, DayPlan edited, List<string> lines)
    {
        foreach (OneOffDay day in edited.OneOffDays)
        {
            OneOffDay? old = basePlan.OneOffDays.FirstOrDefault(candidate => candidate.Date == day.Date);
            if (old is null)
            {
                lines.Add($"Added a one-off day: {DateText(day.Date)}" + (day.Note is null ? "" : $" ({day.Note})"));
            }
            else if (old.Note != day.Note || !old.Template.Parts.SequenceEqual(day.Template.Parts))
            {
                lines.Add($"Changed the one-off day {DateText(day.Date)}");
            }
        }

        foreach (OneOffDay removed in basePlan.OneOffDays.Where(old => edited.OneOffDays.All(day => day.Date != old.Date)))
        {
            lines.Add($"Removed the one-off day {DateText(removed.Date)}");
        }
    }

    private static string Name(PlanPart part, DayPlan plan)
    {
        return part.Label ?? plan.Category(part.CategoryId)?.Name ?? "A part";
    }

    private static string Times(PlanPart part)
    {
        return $"{part.Start.ToString("t", CultureInfo.CurrentCulture)}–{part.End.ToString("t", CultureInfo.CurrentCulture)}";
    }

    private static string DateText(DateOnly date)
    {
        return date.ToString("ddd M/d", CultureInfo.CurrentCulture);
    }

    private static string DayName(DayOfWeek day)
    {
        return CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(day);
    }

    /// <summary>Monday first, as the phone lists days.</summary>
    private static int MondayFirst(DayOfWeek day)
    {
        return ((int)day + 6) % 7;
    }
}
