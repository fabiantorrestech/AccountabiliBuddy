using System.Text.Json;
using System.Text.Json.Serialization;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// A label (ADR 76) as the sealed sync files carry it, merged item by item by <see cref="SharedListMerge"/>: the newest version
/// wins. The name is the user's text: sealed, never logged.
/// </summary>
public sealed record SyncTaskLabel(TaskLabel Label) : ISharedItem
{
    [JsonIgnore]
    public Guid Id => Label.Id;

    [JsonIgnore]
    public DateTimeOffset EditedAt => Label.EditedAt;

    [JsonIgnore]
    public DateTimeOffset? DeletedAt => Label.DeletedAt;

    [JsonIgnore]
    public double Position => Label.Position;

    [JsonIgnore]
    public string TieBreak => JsonSerializer.Serialize(this, SyncJson.Options);

    public ISharedItem WithStamps(DateTimeOffset editedAt, DateTimeOffset? deletedAt)
    {
        return this with { Label = Label with { EditedAt = editedAt, DeletedAt = deletedAt } };
    }
}
