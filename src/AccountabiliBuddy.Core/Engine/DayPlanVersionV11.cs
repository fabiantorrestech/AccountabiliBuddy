using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// The plan version the schema v11 app computed (<see cref="DayPlanVersion"/> before ADR 47): a SHA-256 of the plan in
/// its v11 shape (workdays, workday, otherDay). Used once after the upgrade, to recognise a version stored by the old
/// app as the version of the plan that was then migrated unchanged, so it can be re-stamped without counting as an edit.
/// </summary>
public static class DayPlanVersionV11
{
    /// <returns>
    /// The v11 version of <paramref name="plan"/>'s two-template view (week 1's workdays, the Workday and Off day
    /// templates, the one-off days); null when the plan is no longer that shape (more templates or a cycle of more than one week),
    /// since the v11 app could not have stamped it.
    /// </returns>
    public static string? Of(DayPlan plan)
    {
        if (plan.Templates.Count != 2 || plan.Cycle.Weeks.Count > 1 || plan.TemplateById(DayPlan.WorkdayTemplateId) is null
            || plan.TemplateById(DayPlan.OffDayTemplateId) is null)
        {
            return null;
        }

        var shape = new V11Plan(
            plan.Categories,
            plan.Workdays.OrderBy(day => day).ToList(),
            Template(plan.Workday),
            Template(plan.OtherDay),
            plan.OneOffDays.OrderBy(day => day.Date).Select(day => new V11OneOffDay(day.Id, day.Date, Template(day.Template), day.Note)).ToList());
        string json = ProfileDocumentSerializer.SerializeV11Shape(shape).GetRawText();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static V11Template Template(DayTemplate template)
    {
        return new V11Template(template.Parts.OrderBy(part => part.Start).ThenBy(part => part.Id).ToList(), template.Blocks);
    }

    internal sealed record V11Plan(
        IReadOnlyList<DayCategory> Categories,
        IReadOnlyList<DayOfWeek> Workdays,
        V11Template Workday,
        V11Template OtherDay,
        IReadOnlyList<V11OneOffDay> OneOffDays);

    internal sealed record V11Template(
        IReadOnlyList<PlanPart> Parts,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BlockRules? Blocks);

    internal sealed record V11OneOffDay(Guid Id, DateOnly Date, V11Template Template, string? Note);
}
