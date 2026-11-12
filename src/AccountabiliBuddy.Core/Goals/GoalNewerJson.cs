using System.Text;
using System.Text.Json;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// ADR 79 forward compatibility for goal lists (the stored lists, a goal's source rules, a review's decisions): an item that holds a
/// value this app doesn't know (<see cref="GoalNewerValueException"/>) is kept as written (<see cref="GoalNewerItem"/>) instead of
/// refusing the whole list, and written back at its place; anything else that can't be read still fails the list (damaged). The
/// phone's <c>GoalJson</c> follows the same rule.
/// </summary>
public static class GoalNewerJson
{
    /// <summary>The newer items <paramref name="list"/> carries (none for a list built in code).</summary>
    public static IReadOnlyList<GoalNewerItem> NewerOf<T>(IReadOnlyList<T>? list)
    {
        return list is GoalNewerList<T> carrier ? carrier.Newer : [];
    }

    /// <summary><paramref name="known"/> carrying <paramref name="newer"/> (just the known items when there are none).</summary>
    public static IReadOnlyList<T> WithNewer<T>(IReadOnlyList<T> known, IReadOnlyList<GoalNewerItem> newer)
    {
        IReadOnlyList<T> plain = known is GoalNewerList<T> carrier ? carrier.Known : known;
        return newer.Count == 0 ? plain : new GoalNewerList<T>(plain, newer);
    }

    /// <summary>
    /// Reads a JSON array of goal items: the ones this app knows, and the newer ones kept as written with their places. A JSON null is an
    /// empty list and a null item is skipped (as before); a non-array, or an item damaged in any other way, throws <see cref="JsonException"/>.
    /// </summary>
    public static (List<T> Known, List<GoalNewerItem> Newer) ReadList<T>(JsonElement array, JsonSerializerOptions options)
    {
        var known = new List<T>();
        var newer = new List<GoalNewerItem>();
        if (array.ValueKind == JsonValueKind.Null)
        {
            return (known, newer);
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("A goal list must be a JSON array.");
        }

        int index = 0;
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Null)
            {
                try
                {
                    if (item.Deserialize<T>(options) is { } read)
                    {
                        known.Add(read);
                    }
                }
                catch (GoalNewerValueException) when (item.ValueKind == JsonValueKind.Object)
                {
                    newer.Add(new GoalNewerItem(index, item.Clone()));
                }
            }

            index++;
        }

        return (known, newer);
    }

    /// <summary>Reads a JSON text of goal items (<see cref="ReadList{T}(JsonElement, JsonSerializerOptions)"/>).</summary>
    public static (List<T> Known, List<GoalNewerItem> Newer) ReadList<T>(string json, JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return ReadList<T>(document.RootElement, options);
    }

    /// <summary>
    /// Writes <paramref name="known"/> with <paramref name="newer"/> back at their places (a place past the end goes last), so a list read
    /// and written unchanged comes out in its order; with no newer items it is exactly what the serializer writes for the list.
    /// </summary>
    public static void WriteList<T>(Utf8JsonWriter writer, IReadOnlyList<T> known, IReadOnlyList<GoalNewerItem> newer, JsonSerializerOptions options)
    {
        List<GoalNewerItem> waiting = newer.OrderBy(item => item.Index).ToList();
        int nextNewer = 0;
        int written = 0;
        writer.WriteStartArray();
        foreach (T item in known)
        {
            while (nextNewer < waiting.Count && waiting[nextNewer].Index <= written)
            {
                waiting[nextNewer++].Json.WriteTo(writer);
                written++;
            }

            JsonSerializer.Serialize(writer, item, options);
            written++;
        }

        while (nextNewer < waiting.Count)
        {
            waiting[nextNewer++].Json.WriteTo(writer);
        }

        writer.WriteEndArray();
    }

    /// <summary>The JSON text <see cref="WriteList{T}(Utf8JsonWriter, IReadOnlyList{T}, IReadOnlyList{GoalNewerItem}, JsonSerializerOptions)"/> writes.</summary>
    public static string WriteList<T>(IReadOnlyList<T> known, IReadOnlyList<GoalNewerItem> newer, JsonSerializerOptions options)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = options.Encoder, Indented = options.WriteIndented }))
        {
            WriteList(writer, known, newer, options);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
