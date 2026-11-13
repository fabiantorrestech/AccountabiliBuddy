using System.Text.Json;
using System.Text.Json.Nodes;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// A goal list item kept as written because it holds a value this app doesn't know (<see cref="GoalNewerItem"/>, ADR 79), taking part in
/// the merge by its id and stamps (<see cref="GoalSyncMerge"/>): a newer version of it wins over a known copy and the other way round,
/// a tombstone removes it, and it is written back as it came. Only an item whose id and edit time read (<see cref="TryRead"/>) takes
/// part; any other is carried along unchanged.
/// </summary>
public sealed class GoalRawItem : ISharedItem
{
    private GoalRawItem(JsonElement json, Guid id, DateTimeOffset editedAt, DateTimeOffset? deletedAt, double position)
    {
        Json = json;
        Id = id;
        EditedAt = editedAt;
        DeletedAt = deletedAt;
        Position = position;
    }

    /// <summary>The item as written.</summary>
    public JsonElement Json { get; }

    public Guid Id { get; }

    public DateTimeOffset EditedAt { get; }

    public DateTimeOffset? DeletedAt { get; }

    public double Position { get; }

    public string TieBreak => Json.GetRawText();

    /// <summary>The item's id, edit time, deletion time and place, when its id and edit time read; null otherwise.</summary>
    public static GoalRawItem? TryRead(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object
            || !json.TryGetProperty("id", out JsonElement idValue) || idValue.ValueKind != JsonValueKind.String || !idValue.TryGetGuid(out Guid id)
            || !json.TryGetProperty("editedAt", out JsonElement editedValue) || editedValue.ValueKind != JsonValueKind.String
            || !editedValue.TryGetDateTimeOffset(out DateTimeOffset editedAt))
        {
            return null;
        }

        DateTimeOffset? deletedAt = null;
        if (json.TryGetProperty("deletedAt", out JsonElement deletedValue) && deletedValue.ValueKind == JsonValueKind.String)
        {
            if (!deletedValue.TryGetDateTimeOffset(out DateTimeOffset deleted))
            {
                return null;
            }

            deletedAt = deleted;
        }

        double position = json.TryGetProperty("position", out JsonElement positionValue) && positionValue.ValueKind == JsonValueKind.Number
            && positionValue.TryGetDouble(out double place) ? place : 0;
        return new GoalRawItem(json.Clone(), id, editedAt, deletedAt, position);
    }

    public ISharedItem WithStamps(DateTimeOffset editedAt, DateTimeOffset? deletedAt)
    {
        JsonObject node = JsonNode.Parse(Json.GetRawText())!.AsObject();
        node["editedAt"] = JsonSerializer.SerializeToNode(editedAt, SyncJson.Options);
        if (deletedAt is DateTimeOffset deleted)
        {
            node["deletedAt"] = JsonSerializer.SerializeToNode(deleted, SyncJson.Options);
        }
        else
        {
            node.Remove("deletedAt");
        }

        using JsonDocument document = JsonDocument.Parse(node.ToJsonString(SyncJson.Options));
        return new GoalRawItem(document.RootElement.Clone(), Id, editedAt, deletedAt, Position);
    }
}
