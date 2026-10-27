using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// The plan version the schema v12 app computed (<see cref="DayPlanVersion"/> before the repeating cycle, ADR 47): a SHA-256
/// of the plan with its week map as "week": a week A, an optional week B and the Monday B starts on. Used once after the
/// upgrade, to recognise a version stored by the v12 app as the version of the plan that was then migrated unchanged, so it
/// can be re-stamped without counting as an edit.
/// </summary>
public static class DayPlanVersionV12
{
    /// <returns>
    /// The v12 version of <paramref name="plan"/>'s week map (week 1 as A, week 2 as B when there are two); null when the
    /// plan is no longer that shape (more than two weeks), since the v12 app could not have stamped it.
    /// </returns>
    public static string? Of(DayPlan plan)
    {
        if (plan.Cycle.Weeks.Count > 2)
        {
            return null;
        }

        // The migration made week B start the Monday before week 1's start (v12 B start + 7 days = week 1's start).
        bool alternating = plan.Cycle.Weeks.Count == 2;
        var shape = new V12Plan(
            plan.Categories,
            plan.Templates.OrderBy(template => template.Id).Select(template => template with { Template = Ordered(template.Template) }).ToList(),
            new V12Week(Ordered(plan.Cycle.Weeks[0]), alternating ? Ordered(plan.Cycle.Weeks[1]) : null, alternating ? plan.Cycle.StartsOn.AddDays(-7) : null),
            plan.OneOffDays.OrderBy(day => day.Date).Select(day => day with { Template = Ordered(day.Template) }).ToList());
        string json = ProfileDocumentSerializer.SerializeV12Shape(shape).GetRawText();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static DayTemplate Ordered(DayTemplate template)
    {
        return template with { Parts = template.Parts.OrderBy(part => part.Start).ThenBy(part => part.Id).ToList() };
    }

    private static List<WeekdayTemplate> Ordered(IReadOnlyList<WeekdayTemplate> week)
    {
        return week.OrderBy(entry => ((int)entry.Day + 6) % 7).ToList();
    }

    internal sealed record V12Plan(
        IReadOnlyList<DayCategory> Categories,
        IReadOnlyList<PlanTemplate> Templates,
        V12Week Week,
        IReadOnlyList<OneOffDay> OneOffDays);

    internal sealed record V12Week(
        IReadOnlyList<WeekdayTemplate> A,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<WeekdayTemplate>? B = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? BStartsOn = null);
}
