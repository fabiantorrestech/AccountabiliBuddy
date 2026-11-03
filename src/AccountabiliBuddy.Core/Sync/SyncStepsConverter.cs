using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// Reads <see cref="SyncFile.Steps"/> leniently, as the phone does: anything that can't be read (not an object, no goal or read time,
/// a day with a bad date or count) is left out, so a wrong type in this optional field never makes a whole sync file unreadable.
/// Keeps at most <see cref="SyncSteps.MaxDays"/> days, the newest, oldest first. Written as {goal, readAt, days: [{date, count}]}.
/// </summary>
public sealed class SyncStepsConverter : JsonConverter<SyncSteps>
{
    public override SyncSteps? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !TryNumber(root, "goal", out int goal) || goal < 1 || goal > SyncSteps.MaxGoal
            || !root.TryGetProperty("readAt", out JsonElement readAtText) || readAtText.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParse(readAtText.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset readAt))
        {
            return null;
        }

        var days = new Dictionary<DateOnly, int>();
        if (root.TryGetProperty("days", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in list.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("date", out JsonElement dateText) && dateText.ValueKind == JsonValueKind.String
                    && DateOnly.TryParseExact(dateText.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
                    && TryNumber(item, "count", out int count) && count >= 0)
                {
                    days[date] = count;
                }
            }
        }

        List<SyncStepDay> kept = days.OrderBy(pair => pair.Key).TakeLast(SyncSteps.MaxDays).Select(pair => new SyncStepDay(pair.Key, pair.Value)).ToList();
        return new SyncSteps(goal, readAt, kept);
    }

    public override void Write(Utf8JsonWriter writer, SyncSteps value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("goal", value.Goal);
        writer.WriteString("readAt", value.ReadAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture));
        writer.WriteStartArray("days");
        foreach (SyncStepDay day in value.Days.TakeLast(SyncSteps.MaxDays))
        {
            writer.WriteStartObject();
            writer.WriteString("date", day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            writer.WriteNumber("count", day.Count);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static bool TryNumber(JsonElement parent, string name, out int value)
    {
        value = 0;
        return parent.TryGetProperty(name, out JsonElement element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value);
    }
}
