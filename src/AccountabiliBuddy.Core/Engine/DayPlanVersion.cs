using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// A short fingerprint of a day plan, so the PC and a paired device can tell whether they hold the same plan (ADR 19):
/// a SHA-256 of its JSON with templates in id order, parts in time order, week maps in weekday order (Monday first) and
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
            Week = plan.Week with { A = Ordered(plan.Week.A), B = plan.Week.B is null ? null : Ordered(plan.Week.B) },
            OneOffDays = plan.OneOffDays.OrderBy(day => day.Date).Select(day => day with { Template = Ordered(day.Template) }).ToList(),
        };
        string json = ProfileDocumentSerializer.SerializeDayPlanV12(normalized).GetRawText();
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
}
