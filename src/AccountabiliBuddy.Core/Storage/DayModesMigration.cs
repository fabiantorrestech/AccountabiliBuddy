using System.Text.Json;
using System.Text.Json.Nodes;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Storage;

/// <summary>
/// Profile schema v11 to v12 (ADR 47): the day plan's "workday" and "otherDay" templates become two named templates
/// ("Workday" and "Off day", the fixed ids <see cref="DayPlan.WorkdayTemplateId"/> and <see cref="DayPlan.OffDayTemplateId"/>)
/// and "workdays" becomes week A of the week map, all seven weekdays pointing at one of the two. There is no week B and
/// no mode other than Schedule, so every date resolves to exactly the template it did before. Done on the JSON, so the
/// v11 shape needs no types of its own; the phone does the same in ProfileDocumentParser.
/// </summary>
public static class DayModesMigration
{
    private static readonly string[] DayNames = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];

    /// <summary>The document with its day plan upgraded when it is still in the v11 shape; other documents as they are.</summary>
    public static string UpgradeDocument(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject document || document["dayPlan"] is not JsonObject plan || !IsLegacy(plan))
        {
            return json;
        }

        Upgrade(plan);
        return document.ToJsonString();
    }

    /// <summary>True for a day plan in the v11 shape: a "workday" template and no "templates".</summary>
    public static bool IsLegacy(JsonObject plan)
    {
        return plan["templates"] is null && plan["workday"] is not null;
    }

    /// <summary>Rewrites a v11 day plan into the v12 shape, in place.</summary>
    public static void Upgrade(JsonObject plan)
    {
        JsonNode workday = TakeTemplate(plan, "workday");
        JsonNode otherDay = TakeTemplate(plan, "otherDay");
        HashSet<DayOfWeek> workdays = ReadWorkdays(plan["workdays"]);
        plan.Remove("workdays");

        plan["templates"] = new JsonArray
        {
            Template(DayPlan.WorkdayTemplateId, "Workday", workday),
            Template(DayPlan.OffDayTemplateId, "Off day", otherDay),
        };

        var week = new JsonArray();
        foreach (DayOfWeek day in DayPlan.MondayFirst)
        {
            week.Add(new JsonObject
            {
                ["day"] = DayNames[((int)day + 6) % 7],
                ["templateId"] = (workdays.Contains(day) ? DayPlan.WorkdayTemplateId : DayPlan.OffDayTemplateId).ToString(),
            });
        }

        plan["week"] = new JsonObject { ["a"] = week };
    }

    private static JsonNode TakeTemplate(JsonObject plan, string name)
    {
        JsonNode? template = plan[name];
        plan.Remove(name);
        return template ?? new JsonObject { ["parts"] = new JsonArray() };
    }

    private static JsonObject Template(Guid id, string name, JsonNode template)
    {
        return new JsonObject { ["id"] = id.ToString(), ["name"] = name, ["template"] = template };
    }

    // Weekdays are written as camelCase names; a number (the enum's value) is read too, to be safe.
    private static HashSet<DayOfWeek> ReadWorkdays(JsonNode? node)
    {
        var days = new HashSet<DayOfWeek>();
        if (node is not JsonArray array)
        {
            return days;
        }

        foreach (JsonNode? item in array)
        {
            string? text = item?.GetValueKind() == JsonValueKind.String ? item.GetValue<string>() : item?.ToJsonString();
            if (text is not null && Enum.TryParse(text, ignoreCase: true, out DayOfWeek day) && Enum.IsDefined(day))
            {
                days.Add(day);
            }
        }

        return days;
    }
}
