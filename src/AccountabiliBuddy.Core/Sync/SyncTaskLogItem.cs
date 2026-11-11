using System.Text.Json;
using System.Text.Json.Serialization;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// A History entry (ADR 76: finished, archived or deleted, with the task's snapshot, 30 days) as the sealed sync files carry it
/// in <see cref="SyncFile.TaskLog"/>, merged item by item by <see cref="SharedListMerge"/>. Not ADR 72's session records
/// (<see cref="SyncTaskHistoryEntry"/>). The snapshot's text is sealed, never logged.
/// </summary>
public sealed record SyncTaskLogItem(TaskHistoryEntry Entry) : ISharedItem
{
    [JsonIgnore]
    public Guid Id => Entry.Id;

    [JsonIgnore]
    public DateTimeOffset EditedAt => Entry.EditedAt;

    [JsonIgnore]
    public DateTimeOffset? DeletedAt => Entry.DeletedAt;

    /// <summary>Newest first is the UI's order; the list itself goes by when it happened.</summary>
    [JsonIgnore]
    public double Position => Entry.When.ToUnixTimeMilliseconds();

    [JsonIgnore]
    public string TieBreak => JsonSerializer.Serialize(this, SyncJson.Options);

    public ISharedItem WithStamps(DateTimeOffset editedAt, DateTimeOffset? deletedAt)
    {
        return this with { Entry = Entry with { EditedAt = editedAt, DeletedAt = deletedAt } };
    }
}
