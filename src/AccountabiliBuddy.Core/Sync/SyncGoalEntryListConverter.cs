using System.Text.Json;
using System.Text.Json.Serialization;
using AccountabiliBuddy.Core.Goals;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// Reads and writes a list of goal entries as compact rows (ADR 79 size decision, GS1b; <see cref="GoalEntryRows"/>): the sync file's
/// <c>goalEntries</c> and the goal history files' <c>entries</c>. Written as rows (the object form for an entry a row can't hold exactly),
/// a newer app's items back at their places. Read leniently and forward compatibly, like <see cref="SyncGoalListConverter{T}"/>: both
/// forms are taken (a row is an object with <c>g</c>, anything else the GS1a object form); a value that isn't an array is ignored (null); an
/// item holding a value this app doesn't know (a source, a row field) is kept as written at its place; any other item that can't be read is
/// left out (leaving an entry out of a merge never deletes it).
/// </summary>
public sealed class SyncGoalEntryListConverter : JsonConverter<IReadOnlyList<GoalEntry>?>
{
    public override IReadOnlyList<GoalEntry>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        return ReadList(document.RootElement, options);
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<GoalEntry>? value, JsonSerializerOptions options)
    {
        IReadOnlyList<GoalEntry> list = value ?? [];
        GoalNewerJson.WriteList(writer, list, GoalNewerJson.NewerOf(list), (output, entry) => GoalEntryRows.Write(output, entry, options));
    }

    /// <summary>Reads a JSON array of entries in either form (see the class summary); null when it isn't an array.</summary>
    public static IReadOnlyList<GoalEntry>? ReadList(JsonElement array, JsonSerializerOptions options)
    {
        if (array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var known = new List<GoalEntry>();
        var newer = new List<GoalNewerItem>();
        int index = 0;
        foreach (JsonElement element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            try
            {
                GoalEntry? entry = GoalEntryRows.IsRow(element) ? GoalEntryRows.Read(element) : element.Deserialize<GoalEntry>(options);
                if (entry is not null)
                {
                    known.Add(entry);
                    index++;
                }
            }
            catch (GoalNewerValueException)
            {
                newer.Add(new GoalNewerItem(index, element.Clone()));
                index++;
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or ArgumentException or FormatException)
            {
                // An entry this app can't read is left out.
            }
        }

        return GoalNewerJson.WithNewer(known, newer);
    }
}
