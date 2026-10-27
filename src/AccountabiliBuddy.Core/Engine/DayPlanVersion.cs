using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// A short fingerprint of a day plan, so the PC and a paired device can tell whether they hold the same plan (ADR 19):
/// a SHA-256 of its JSON with templates in id order, parts in time order, each week of the cycle in weekday order (Monday first) and
/// one-off days in date order, so the same plan always gets the same version however its lists happen to be ordered.
/// Only the PC computes it (the phone sends its plan as JSON and the PC hashes what it read), so the phone's JSON needs
/// to carry the same fields, not to be written in the same order.
/// </summary>
public static class DayPlanVersion
{
    /// <returns>64 lowercase hex characters.</returns>
    public static string Of(DayPlan plan)
    {
        DayPlan normalized = plan with
        {
            Templates = plan.Templates.OrderBy(template => template.Id).Select(template => template with { Template = Ordered(template.Template) }).ToList(),
            Cycle = plan.Cycle with { Weeks = plan.Cycle.Weeks.Select(week => (IReadOnlyList<WeekdayTemplate>)Ordered(week)).ToList() },
            OneOffDays = plan.OneOffDays.OrderBy(day => day.Date).Select(day => day with { Template = Ordered(day.Template) }).ToList(),
        };
        string json = ProfileDocumentSerializer.SerializeDayPlanV13(normalized).GetRawText();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    /// <summary>
    /// True when <paramref name="version"/> is the version an earlier schema's app stamped on this same plan (before
    /// the day plan modes or before the repeating cycle), the plan having been migrated unchanged. Used once after an
    /// upgrade, to re-stamp the stored version without counting it as an edit.
    /// </summary>
    public static bool IsEarlierSchemaVersionOf(string version, DayPlan plan)
    {
        return version == DayPlanVersionV11.Of(plan) || version == DayPlanVersionV12.Of(plan);
    }

    private static DayTemplate Ordered(DayTemplate template)
    {
        return template with { Parts = template.Parts.OrderBy(part => part.Start).ThenBy(part => part.Id).ToList() };
    }

    private static List<WeekdayTemplate> Ordered(IReadOnlyList<WeekdayTemplate> week)
    {
        return week.OrderBy(entry => ((int)entry.Day + 6) % 7).ToList();
    }
}
