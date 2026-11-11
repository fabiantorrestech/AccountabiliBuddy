using System.Text.Json;
using System.Text.Json.Serialization;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// A saved view (ADR 76) as the sealed sync files carry it, merged item by item by <see cref="SharedListMerge"/>. Never carries
/// search words (they stay on the PC that saved them).
/// </summary>
public sealed record SyncTaskView(TaskView View) : ISharedItem
{
    [JsonIgnore]
    public Guid Id => View.Id;

    [JsonIgnore]
    public DateTimeOffset EditedAt => View.EditedAt;

    [JsonIgnore]
    public DateTimeOffset? DeletedAt => View.DeletedAt;

    [JsonIgnore]
    public double Position => View.Position;

    [JsonIgnore]
    public string TieBreak => JsonSerializer.Serialize(this, SyncJson.Options);

    public ISharedItem WithStamps(DateTimeOffset editedAt, DateTimeOffset? deletedAt)
    {
        return this with { View = View with { EditedAt = editedAt, DeletedAt = deletedAt } };
    }
}
