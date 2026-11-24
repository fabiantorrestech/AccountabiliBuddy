using System.Text.Json;
using System.Text.Json.Serialization;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// A label group (ADR 93) as the sealed sync files carry it in <see cref="SyncFile.LabelGroups"/>, merged item by item by
/// <see cref="TaskLabelGroupMerge"/>: the newest version wins, tombstones kept for good. The name is the user's text: sealed, never logged.
/// </summary>
public sealed record SyncTaskLabelGroup(TaskLabelGroup Group) : ISharedItem
{
    [JsonIgnore]
    public Guid Id => Group.Id;

    [JsonIgnore]
    public DateTimeOffset EditedAt => Group.EditedAt;

    [JsonIgnore]
    public DateTimeOffset? DeletedAt => Group.DeletedAt;

    [JsonIgnore]
    public double Position => Group.Position;

    [JsonIgnore]
    public string TieBreak => JsonSerializer.Serialize(this, SyncJson.Options);

    public ISharedItem WithStamps(DateTimeOffset editedAt, DateTimeOffset? deletedAt)
    {
        return this with { Group = Group with { EditedAt = editedAt, DeletedAt = deletedAt } };
    }
}
