using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// A short fingerprint of a day plan, so the PC and a paired device can tell whether they hold the same plan (ADR 19):
/// a SHA-256 of its JSON with parts in time order and one-off days in date order, so the same plan always gets the
/// same version however its lists happen to be ordered.
/// </summary>
public static class DayPlanVersion
{
    /// <returns>64 lowercase hex characters.</returns>
    public static string Of(DayPlan plan)
    {
        DayPlan normalized = plan with
        {
            Workdays = plan.Workdays.OrderBy(day => day).ToList(),
            Workday = Ordered(plan.Workday),
            OtherDay = Ordered(plan.OtherDay),
            OneOffDays = plan.OneOffDays.OrderBy(day => day.Date).Select(day => day with { Template = Ordered(day.Template) }).ToList(),
        };
        string json = ProfileDocumentSerializer.SerializeDayPlan(normalized).GetRawText();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static DayTemplate Ordered(DayTemplate template)
    {
        return new DayTemplate(template.Parts.OrderBy(part => part.Start).ThenBy(part => part.Id).ToList());
    }
}
