using System.Text.Json;
using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// Reads an optional list of a sync file leniently (ADR 76's parts): a value that isn't an array is ignored (null), and an entry
/// that can't be read as <typeparamref name="T"/> (a newer app's unknown bin, a wrong type) is skipped, so one odd entry never
/// makes a whole sync file unreadable. Skipping only ever leaves an item out of a merge, which never deletes it. Written as an
/// ordinary array.
/// </summary>
public sealed class SyncLenientListConverter<T> : JsonConverter<IReadOnlyList<T>?>
    where T : class
{
    public override IReadOnlyList<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var items = new List<T>();
        foreach (JsonElement element in document.RootElement.EnumerateArray())
        {
            try
            {
                if (element.ValueKind == JsonValueKind.Object && element.Deserialize<T>(options) is T item)
                {
                    items.Add(item);
                }
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or ArgumentException or FormatException)
            {
                // An entry this app can't read is left out.
            }
        }

        return items;
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<T>? value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (T item in value ?? [])
        {
            JsonSerializer.Serialize(writer, item, options);
        }

        writer.WriteEndArray();
    }
}
