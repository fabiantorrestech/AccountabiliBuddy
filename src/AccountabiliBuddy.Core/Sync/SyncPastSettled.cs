using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// What a device did with a task a past session left open (ADR 72), shared item by item like ADR 48's lists, so every device
/// stops listing it as unfinished and the PC whose history holds it brings its record in step. <see cref="Id"/> is the past
/// task's own id. Newest edit wins.
/// </summary>
/// <param name="State">One of <see cref="Moved"/>, <see cref="Done"/>, <see cref="Open"/>, <see cref="Deleted"/>, <see cref="Local"/>.</param>
/// <param name="LocalTo">With <see cref="Local"/>: the device that keeps the task now.</param>
public sealed record SyncPastSettled(
    Guid Id,
    string State,
    DateTimeOffset EditedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? DeletedAt = null,
    double Position = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? LocalTo = null) : ISharedItem
{
    /// <summary>Copied to another list (Later, Today, Come back to): no longer left open.</summary>
    public const string Moved = "moved";

    /// <summary>Ticked off.</summary>
    public const string Done = "done";

    /// <summary>Open again (an untick, or a local task made synced again).</summary>
    public const string Open = "open";

    /// <summary>Deleted (it goes to its PC's Recently deleted).</summary>
    public const string Deleted = "deleted";

    /// <summary>Kept on one device only (<see cref="LocalTo"/>).</summary>
    public const string Local = "local";

    /// <summary>How long a record is kept: as long as the history it speaks of.</summary>
    public static readonly TimeSpan Kept = TimeSpan.FromDays(90);

    /// <summary>Whether the task should no longer be listed as unfinished.</summary>
    [JsonIgnore]
    public bool HidesTask => State is Moved or Done or Deleted or Local;

    [JsonIgnore]
    public string TieBreak => string.Join("\u0001", new[] { State }.Concat(SharedItemTie.Extra(null, LocalTo)));

    public ISharedItem WithStamps(DateTimeOffset editedAt, DateTimeOffset? deletedAt)
    {
        return this with { EditedAt = editedAt, DeletedAt = deletedAt };
    }
}
