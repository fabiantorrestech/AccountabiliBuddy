using System.Globalization;
using System.Text.Json;

namespace AccountabiliBuddy.App.Services.Beeminder;

/// <summary>
/// Reads Beeminder's goals.json (ADR 43) into <see cref="BeeminderGoal"/>s, soonest due first. Missing fields are
/// tolerated: the title falls back to the slug, "what's needed" to baremin + units, the safe days to the days left
/// before the due time; a goal with no due time is left out.
/// </summary>
public static class BeeminderGoalsParser
{
    public static IReadOnlyList<BeeminderGoal> Parse(JsonElement root, DateTimeOffset now)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var goals = new List<BeeminderGoal>();
        foreach (JsonElement item in root.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || Number(item, "losedate") is not double loseSeconds)
            {
                continue;
            }

            DateTimeOffset dueAt;
            try
            {
                dueAt = DateTimeOffset.FromUnixTimeSeconds((long)loseSeconds);
            }
            catch (ArgumentOutOfRangeException)
            {
                continue;
            }

            string title = Text(item, "title") is { Length: > 0 } named ? named : Text(item, "slug") ?? "Goal";
            int safeDays = Number(item, "safebuf") is double buffer
                ? (int)Math.Floor(buffer)
                : (int)Math.Floor((dueAt - now).TotalDays);
            goals.Add(new BeeminderGoal(title, NeededOf(item), safeDays, dueAt));
        }

        return goals.OrderBy(goal => goal.DueAt).ToList();
    }

    // "limsum" reads best ("+2 within 1 day"); "baremin" with the goal's units is the fallback.
    private static string NeededOf(JsonElement item)
    {
        if (Text(item, "limsum") is { Length: > 0 } summary)
        {
            return summary;
        }

        if (Text(item, "baremin") is { Length: > 0 } bareMinimum)
        {
            return Text(item, "gunits") is { Length: > 0 } units ? $"{bareMinimum} {units}" : bareMinimum;
        }

        return string.Empty;
    }

    private static string? Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;

    private static double? Number(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : null;
    }
}
