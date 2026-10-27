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
        DayTypes(basePlan, edited, lines);
        Week(basePlan, edited, lines);
        foreach (PlanTemplate after in edited.Templates)
        {
            if (basePlan.Templates.FirstOrDefault(old => old.Id == after.Id) is PlanTemplate before)
            {
                Template(Title(after), before.Template, after.Template, basePlan, edited, lines);
            }
        }

        OneOffDays(basePlan, edited, lines);
        return lines.Count > 0 ? lines : new[] { "No changes" };
    }

    // The two migrated templates keep the wording the plan always had.
    private static string Title(PlanTemplate template)
    {
        return template.Id == DayPlan.WorkdayTemplateId ? "Workdays" : template.Id == DayPlan.OffDayTemplateId ? "Off-Days" : template.Name;
    }

    private static void DayTypes(DayPlan basePlan, DayPlan edited, List<string> lines)
    {
        foreach (PlanTemplate template in edited.Templates)
        {
            if (basePlan.Templates.FirstOrDefault(old => old.Id == template.Id) is not PlanTemplate before)
            {
                lines.Add($"New day type: {template.Name}");
            }
            else if (before.Name != template.Name)
            {
                lines.Add($"Day type {before.Name} renamed to {template.Name}");
            }
        }

        foreach (PlanTemplate removed in basePlan.Templates.Where(old => edited.Templates.All(template => template.Id != old.Id)))
        {
            lines.Add($"Removed day type: {removed.Name}");
        }
    }

    private static void Week(DayPlan basePlan, DayPlan edited, List<string> lines)
    {
        bool alternating = basePlan.Week.B is not null || edited.Week.B is not null;
        Weekdays("A", basePlan, edited, basePlan.Week.A, edited.Week.A, alternating, lines);
        if (basePlan.Week.B is null && edited.Week.B is not null)
        {
            lines.Add($"Alternating weeks turned on: week B starts {DateText(edited.Week.BStartsOn)}");
        }
        else if (basePlan.Week.B is not null && edited.Week.B is null)
        {
            lines.Add("Alternating weeks turned off");
        }
        else if (edited.Week.B is not null)
        {
            if (basePlan.Week.BStartsOn != edited.Week.BStartsOn)
            {
                lines.Add($"Week B now starts {DateText(edited.Week.BStartsOn)}");
            }

            Weekdays("B", basePlan, edited, basePlan.Week.B!, edited.Week.B, alternating, lines);
        }
    }

    private static void Weekdays(string weekName, DayPlan basePlan, DayPlan edited, IReadOnlyList<WeekdayTemplate> before, IReadOnlyList<WeekdayTemplate> after, bool alternating, List<string> lines)
    {
        foreach (DayOfWeek day in DayPlan.MondayFirst)
        {
            Guid? was = before.FirstOrDefault(entry => entry.Day == day)?.TemplateId;
            Guid? now = after.FirstOrDefault(entry => entry.Day == day)?.TemplateId;
            if (was == now)
            {
                continue;
            }

            Guid wasId = was ?? DayPlan.OffDayTemplateId;
            Guid nowId = now ?? DayPlan.OffDayTemplateId;
            if (weekName == "A" && nowId == DayPlan.WorkdayTemplateId && wasId != nowId)
            {
                lines.Add($"{DayName(day)} is now a workday");
            }
            else if (weekName == "A" && wasId == DayPlan.WorkdayTemplateId && nowId == DayPlan.OffDayTemplateId)
            {
                lines.Add($"{DayName(day)} is no longer a workday");
            }
            else if (wasId != nowId)
            {
                string label = alternating ? $"{DayName(day)} (week {weekName})" : DayName(day);
                lines.Add($"{label}: {DayTypeName(basePlan, wasId)} → {DayTypeName(edited, nowId)}");
            }
        }
    }

    private static string DayTypeName(DayPlan plan, Guid id)
    {
        return plan.Templates.FirstOrDefault(template => template.Id == id)?.Name ?? "another day type";
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

    private static void Template(string title, DayTemplate before, DayTemplate after, DayPlan basePlan, DayPlan edited, List<string> lines)
    {
        if (before.Mode != after.Mode)
        {
            lines.Add($"{title}: now a {after.Mode} day");
        }

        if (before.WorkWindow != after.WorkWindow)
        {
            lines.Add(before.WorkWindow is null ? $"{title}: added a work window {Times(after.WorkWindow!)}"
                : after.WorkWindow is null ? $"{title}: removed the work window"
                : $"{title}: work window {Times(before.WorkWindow)} → {Times(after.WorkWindow)}");
        }

        if (before.BudgetMinutes != after.BudgetMinutes)
        {
            lines.Add($"{title}: budget {Hours(before.BudgetMinutes)} → {Hours(after.BudgetMinutes)}");
        }

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
            else if (old.Note != day.Note || !old.Template.Parts.SequenceEqual(day.Template.Parts)
                || old.Template.Mode != day.Template.Mode || old.Template.WorkWindow != day.Template.WorkWindow
                || old.Template.BudgetMinutes != day.Template.BudgetMinutes)
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

    private static string Times(WorkWindow window)
    {
        return $"{window.Start.ToString("t", CultureInfo.CurrentCulture)}–{window.End.ToString("t", CultureInfo.CurrentCulture)}";
    }

    /// <summary>Minutes as "6h", "6h 30m" or "45m"; "none" when not set.</summary>
    private static string Hours(int? minutes)
    {
        if (minutes is not int total)
        {
            return "none";
        }

        return total % 60 == 0 ? $"{total / 60}h" : total < 60 ? $"{total}m" : $"{total / 60}h {total % 60}m";
    }

    private static string DateText(DateOnly? date)
    {
        return date is DateOnly value ? value.ToString("ddd M/d", CultureInfo.CurrentCulture) : "an unset date";
    }

    private static string DayName(DayOfWeek day)
    {
        return CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(day);
    }

}
