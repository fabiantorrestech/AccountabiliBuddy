using System.Text.Json;
using System.Text.Json.Nodes;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Storage;

/// <summary>
/// The older views of a v13 day plan (ADR 47), written next to the v13 fields so an older app that reads only those still
/// gets a sensible plan instead of empty templates: the v12 "week" (week 1 as A, week 2 as B when the cycle has two weeks, a
/// longer cycle shows week 1 only) and the v11 "workdays", "workday" and "otherDay". Newer readers ignore them when "cycle"
/// (respectively "templates") is there; they are never part of <see cref="Engine.DayPlanVersion"/>.
/// </summary>
/// <remarks>
/// "workday" is the template most weekdays of week 1 follow, other than the Off day template and Free templates (ties:
/// the migrated "Workday", then the first listed); "workdays" are the weekdays following it; "otherDay" is the Off day
/// template. Other templates' weekdays count as off days. A template is written as the parts a v11 reader sees: a work
/// window as its work part, a Budget day as its non-productive windows, a Free day as no parts. A plan migrated from v11
/// projects back to exactly the two templates and workdays it started from.
/// </remarks>
public static class LegacyDayPlan
{
    /// <summary>Adds the v12 and v11 fields to a serialized v13 day plan, in place.</summary>
    public static void AddTo(JsonObject plan, DayPlan source, JsonSerializerOptions options)
    {
        // The v12 week map: a two-week cycle was a week A and a week B starting the Monday before week 1 (the migration's rule, reversed).
        bool alternating = source.Cycle.Weeks.Count == 2;
        plan["week"] = JsonSerializer.SerializeToNode(
            new Engine.DayPlanVersionV12.V12Week(WeekOne(source), alternating ? source.Cycle.Weeks[1] : null, alternating ? source.Cycle.StartsOn.AddDays(-7) : null), options);

        Guid workdayId = WorkdayTemplate(source);
        plan["workdays"] = JsonSerializer.SerializeToNode(
            DayPlan.MondayFirst.Where(day => WeekOne(source).Any(entry => entry.Day == day && entry.TemplateId == workdayId)).ToList(), options);
        plan["workday"] = JsonSerializer.SerializeToNode(Projected(source, workdayId), options);
        plan["otherDay"] = JsonSerializer.SerializeToNode(Projected(source, DayPlan.OffDayTemplateId), options);
    }

    private static IReadOnlyList<WeekdayTemplate> WeekOne(DayPlan plan)
    {
        return plan.Cycle.Weeks.FirstOrDefault() ?? [];
    }

    private static Guid WorkdayTemplate(DayPlan plan)
    {
        var counts = new Dictionary<Guid, int>();
        foreach (WeekdayTemplate entry in WeekOne(plan))
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
