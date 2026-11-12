using System.Text.Json;
using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// A nested goal list property in JSON (ADR 79 forward compatibility): read with <see cref="GoalNewerJson.ReadList{T}(JsonElement, JsonSerializerOptions)"/>
/// (an item with a value this app doesn't know is kept as written, the list still reads) and written with its newer items back at their places.
/// </summary>
public sealed class GoalNewerListConverter<T> : JsonConverter<IReadOnlyList<T>>
{
    public override IReadOnlyList<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        (List<T> known, List<GoalNewerItem> newer) = GoalNewerJson.ReadList<T>(document.RootElement, options);
        return GoalNewerJson.WithNewer(known, newer);
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<T> value, JsonSerializerOptions options)
    {
        GoalNewerJson.WriteList(writer, value, GoalNewerJson.NewerOf(value), options);
    }
}
