using System.Text.Json;
using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// Reads <see cref="SyncFile.Categories"/> leniently, as the phone does: a value that isn't an array is ignored (null), and an entry that
/// isn't an object with a text name is skipped, so a wrong type in this optional list never makes a whole sync file unreadable. Names
/// and colours are cleaned afterwards by <see cref="SyncFileCodec"/>. Written as an ordinary array of {name, color}.
/// </summary>
public sealed class SyncCategoriesConverter : JsonConverter<IReadOnlyList<SyncCategory>?>
{
    public override IReadOnlyList<SyncCategory>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var categories = new List<SyncCategory>();
        foreach (JsonElement item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("name", out JsonElement name) && name.ValueKind == JsonValueKind.String)
            {
                string color = item.TryGetProperty("color", out JsonElement hex) && hex.ValueKind == JsonValueKind.String ? hex.GetString() ?? string.Empty : string.Empty;
                categories.Add(new SyncCategory(name.GetString() ?? string.Empty, color));
            }
        }

        return categories;
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<SyncCategory>? value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (SyncCategory category in value ?? [])
        {
            writer.WriteStartObject();
            writer.WriteString("name", category.Name);
            writer.WriteString("color", category.Color);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }
}
