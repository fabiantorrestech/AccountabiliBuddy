using System.Globalization;
using System.Text.Json;

namespace AccountabiliBuddy.App.Services.Beeminder;

/// <summary>
/// Reads Beeminder's datapoints.json (ADR 84) into <see cref="BeeminderDatapoint"/>s. Only the day stamp ("yyyyMMdd"), the value,
/// the id and the timestamp (for ordering) are looked at; comments and everything else are never read, kept or logged. A datapoint
/// without a readable day or value is skipped.
/// </summary>
public static class BeeminderDatapointsParser
{
    public static IReadOnlyList<BeeminderDatapoint> Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var points = new List<BeeminderDatapoint>();
        foreach (JsonElement item in root.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || Text(item, "daystamp") is not string stamp
                || !DateOnly.TryParseExact(stamp, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day)
                || Number(item, "value") is not double value)
            {
                continue;
            }

            string id = Text(item, "id") ?? string.Empty;
            long order = Number(item, "timestamp") is double stamped ? (long)stamped : 0;
            points.Add(new BeeminderDatapoint(id, day, value, order));
        }

        return points;
    }

    private static string? Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double? Number(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number) && double.IsFinite(number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) && double.IsFinite(parsed)
            ? parsed
            : null;
    }
}
