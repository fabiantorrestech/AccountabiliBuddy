using System.Text.Json;
using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// Reads an optional object of a sync file leniently: a value that isn't an object, or can't be read as <typeparamref name="T"/> (a
/// wrong type from a newer or damaged writer), is ignored (null), so it never makes a whole sync file unreadable. Ignoring it only
/// leaves it out of a merge, which keeps what the receiver has. Written as an ordinary object.
/// </summary>
public sealed class SyncLenientObjectConverter<T> : JsonConverter<T?>
    where T : class
{
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        try
        {
            return document.RootElement.Deserialize<T>(options);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or ArgumentException or FormatException)
        {
            return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        JsonSerializer.Serialize(writer, value, options);
    }
}
