using System.Text.Json.Serialization;
using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// Where a tag is shown (ADR 89), as the sealed sync files carry it in <see cref="SyncFile.TagScopes"/>, merged item by item by
/// <see cref="SharedListMerge"/> (newest version wins, tombstones kept 30 days). Tags are plain names on tasks, so a tag has no
/// record of its own; a tag has a scope only while it is device-only. A tag with no live scope is shared. The id is
/// <see cref="TaskScopes.TagIdFor"/> the name (a namespace of its own), so every device that types "Work" reaches the same scope.
/// The name is the user's text: sealed, never logged, no further than the tag already travels on a task.
/// </summary>
/// <param name="Name">The tag as first written; null on a tombstone.</param>
/// <param name="OnlyOn">The devices that show the tag (device ids, in id order); null on a tombstone.</param>
public sealed record SyncTagScope(
    Guid Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<Guid>? OnlyOn,
    DateTimeOffset EditedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? DeletedAt = null) : ISharedItem
{
    /// <summary>Scopes sit in id order.</summary>
    [JsonIgnore]
    public double Position => 0;

    [JsonIgnore]
    public string TieBreak => string.Join("\u0001", Name ?? string.Empty, TaskRingDevices.Text(OnlyOn));

    public ISharedItem WithStamps(DateTimeOffset editedAt, DateTimeOffset? deletedAt)
    {
        return this with { EditedAt = editedAt, DeletedAt = deletedAt };
    }

    /// <summary>The scope as a tombstone: the tag is shared again; no name or devices left.</summary>
    public SyncTagScope Deleted(DateTimeOffset at)
    {
        return this with { Name = null, OnlyOn = null, EditedAt = at, DeletedAt = at };
    }
}
