using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// A goal enum in JSON (ADR 79 forward compatibility): written by name; read by name (exact, else in any case) or by its number (a
/// number or a number in text), as the phone's <c>GoalJson</c> reads it. A name or number this app doesn't know throws
/// <see cref="GoalNewerValueException"/>, so the list it sits in keeps that item as written; anything else (an object, true) is damaged.
/// </summary>
public sealed class GoalEnumConverter<T> : JsonConverter<T>
    where T : struct, Enum
{
    private static readonly T[] Values = Enum.GetValues<T>();

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => FromText(reader.GetString() ?? string.Empty),
            JsonTokenType.Number => reader.TryGetInt32(out int number) ? FromNumber(number) : throw Newer("a number"),
            _ => throw new JsonException($"A {typeof(T).Name} must be a name or a number."),
        };
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }

    public override T ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return FromText(reader.GetString() ?? string.Empty);
    }

    public override void WriteAsPropertyName(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WritePropertyName(value.ToString());
    }

    private static T FromText(string text)
    {
        string trimmed = text.Trim();
        foreach (T value in Values)
        {
            if (string.Equals(value.ToString(), trimmed, StringComparison.Ordinal))
            {
                return value;
            }
        }

        foreach (T value in Values)
        {
            if (string.Equals(value.ToString(), trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return int.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int number) ? FromNumber(number) : throw Newer("a name");
    }

    private static T FromNumber(int number)
    {
        foreach (T value in Values)
        {
            if (Convert.ToInt32(value, CultureInfo.InvariantCulture) == number)
            {
                return value;
            }
        }

        throw Newer("a number");
    }

    // Never the value itself: the message may reach a log.
    private static GoalNewerValueException Newer(string what)
    {
        return new GoalNewerValueException($"A {typeof(T).Name} holds {what} this version doesn't know (written by a newer version).");
    }
}
