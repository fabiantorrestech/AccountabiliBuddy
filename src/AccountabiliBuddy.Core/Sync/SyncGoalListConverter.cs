using System.Text.Json;
using System.Text.Json.Serialization;
using AccountabiliBuddy.Core.Goals;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// Reads a goal part of a sync file (ADR 79) leniently and forward compatibly: a value that isn't an array is ignored (null); an item
/// holding a value this app doesn't know (<see cref="GoalNewerValueException"/>) is kept as written at its place
/// (<see cref="GoalNewerItem"/>, carried by the list as <see cref="GoalNewerList{T}"/>) and written back; any other item that can't be
/// read is left out, so one damaged item never makes the file unreadable (leaving an item out of a merge never deletes it). Written with
/// its kept items back at their places (<see cref="GoalNewerJson.WriteList{T}(Utf8JsonWriter, IReadOnlyList{T}, IReadOnlyList{GoalNewerItem}, JsonSerializerOptions)"/>).
/// </summary>
public sealed class SyncGoalListConverter<T> : JsonConverter<IReadOnlyList<T>?>
    where T : class
{
    public override IReadOnlyList<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var known = new List<T>();
        var newer = new List<GoalNewerItem>();
        int index = 0;
        foreach (JsonElement element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            try
            {
                if (element.Deserialize<T>(options) is T item)
                {
                    known.Add(item);
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
                // An item this app can't read is left out.
            }
        }

        return GoalNewerJson.WithNewer(known, newer);
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<T>? value, JsonSerializerOptions options)
    {
        IReadOnlyList<T> list = value ?? [];
        GoalNewerJson.WriteList(writer, list, GoalNewerJson.NewerOf(list), options);
    }
}
