using System.Text.Json;
using System.Text.Json.Nodes;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Storage;

/// <summary>
/// The v11 view of a v12 day plan (ADR 47): "workdays", "workday" and "otherDay", written next to the v12 fields so an
/// older app that reads only those still gets a sensible plan instead of empty templates. Newer readers ignore them
/// when "templates" is there; they are never part of <see cref="Engine.DayPlanVersion"/>.
/// </summary>
/// <remarks>
/// "workday" is the template most weekdays of week A follow, other than the Off day template and Free templates (ties:
/// the migrated "Workday", then the first listed); "workdays" are the weekdays following it; "otherDay" is the Off day
/// template. Other templates' weekdays count as off days. A template is written as the parts a v11 reader sees: a work
/// window as its work part, a Budget day as its non-productive windows, a Free day as no parts. A plan migrated from v11
/// projects back to exactly the two templates and workdays it started from.
/// </remarks>
public static class LegacyDayPlan
{
    /// <summary>Adds the v11 fields to a serialized v12 day plan, in place.</summary>
    public static void AddTo(JsonObject plan, DayPlan source, JsonSerializerOptions options)
    {
        Guid workdayId = WorkdayTemplate(source);
        plan["workdays"] = JsonSerializer.SerializeToNode(
            DayPlan.MondayFirst.Where(day => source.Week.A.Any(entry => entry.Day == day && entry.TemplateId == workdayId)).ToList(), options);
        plan["workday"] = JsonSerializer.SerializeToNode(Projected(source, workdayId), options);
        plan["otherDay"] = JsonSerializer.SerializeToNode(Projected(source, DayPlan.OffDayTemplateId), options);
    }

    private static Guid WorkdayTemplate(DayPlan plan)
    {
        var counts = new Dictionary<Guid, int>();
        foreach (WeekdayTemplate entry in plan.Week.A)
        {
            PlanTemplate? template = plan.Templates.FirstOrDefault(candidate => candidate.Id == entry.TemplateId);
            if (template is not null && entry.TemplateId != DayPlan.OffDayTemplateId && template.Template.Mode != DayMode.Free)
            {
                counts[entry.TemplateId] = counts.GetValueOrDefault(entry.TemplateId) + 1;
            }
        }

        // Most weekdays; ties go to the migrated "Workday", then to the first listed template.
        return plan.Templates
            .Where(template => counts.ContainsKey(template.Id))
            .OrderByDescending(template => counts[template.Id])
            .ThenByDescending(template => template.Id == DayPlan.WorkdayTemplateId)
            .Select(template => template.Id)
            .FirstOrDefault(DayPlan.WorkdayTemplateId);
    }

    private static DayTemplate Projected(DayPlan plan, Guid id)
    {
        DayTemplate? stored = plan.TemplateById(id);
        if (stored is null || stored.Mode == DayMode.Free)
        {
            return DayTemplate.Empty;
        }

        // The window becomes its work part; the mode, window and budget are v12 fields a v11 reader doesn't know.
        return new DayTemplate(stored.WithWorkWindowPart(plan.Categories).Parts, stored.Blocks);
    }
}
