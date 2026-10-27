using System.Globalization;
using System.Text.Json.Nodes;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Storage;

/// <summary>
/// Profile schema v12 to v13 (ADR 47): the day plan's week map ("week": a week A, an optional week B and the Monday B
/// starts on) becomes a repeating cycle ("cycle": its weeks and the Monday week 1 starts on).
/// <list type="bullet">
/// <item>Week A only becomes a 1-week cycle, starting on <see cref="DayPlan.DefaultCycleStart"/> (it makes no difference).</item>
/// <item>A and B become a 2-week cycle, A first. Week B was the weeks an even number of weeks from B's start, so week A (week
/// 1) is the weeks an odd number from it: the cycle starts a week after B's start, and every date, before B's start too,
/// follows the same week as before.</item>
/// </list>
/// Done on the JSON, so the v12 shape needs no types of its own; the phone does the same in ProfileDocumentParser.
/// </summary>
public static class DayCycleMigration
{
    /// <summary>The document with its day plan upgraded when it is still in the v12 shape; other documents as they are.</summary>
    public static string UpgradeDocument(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject document || document["dayPlan"] is not JsonObject plan || !IsV12(plan))
        {
            return json;
        }

        Upgrade(plan);
        return document.ToJsonString();
    }

    /// <summary>True for a day plan in the v12 shape: a "week" and no "cycle".</summary>
    public static bool IsV12(JsonObject plan)
    {
        return plan["cycle"] is null && plan["week"] is JsonObject;
    }

    /// <summary>Rewrites a v12 day plan into the v13 shape, in place.</summary>
    public static void Upgrade(JsonObject plan)
    {
        var week = (JsonObject)plan["week"]!;
        plan.Remove("week");

        var weeks = new JsonArray { week["a"]?.DeepClone() ?? new JsonArray() };
        DateOnly startsOn = DayPlan.DefaultCycleStart;
        if (week["b"] is JsonArray weekB && ReadDate(week["bStartsOn"]) is DateOnly bStartsOn)
        {
            weeks.Add(weekB.DeepClone());
            startsOn = bStartsOn.AddDays(7);
        }

        plan["cycle"] = new JsonObject
        {
            ["weeks"] = weeks,
            ["startsOn"] = startsOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };
    }

    private static DateOnly? ReadDate(JsonNode? node)
    {
        return node is JsonValue value && value.TryGetValue(out string? text)
            && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
            ? date
            : null;
    }
}
