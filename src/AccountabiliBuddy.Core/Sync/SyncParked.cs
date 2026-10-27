using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// A come-back (ADR 30, 48), the user's parked thought, as the sealed sync files share it between the user's devices. A
/// ticked one is kept with when it was ticked; a deleted one travels as a tombstone with no text. Only the sealed files
/// carry it; the text is never logged.
/// </summary>
/// <param name="DoneAt">When it was ticked; null while it is open.</param>
public sealed record SyncParked(
    Guid Id,
    string Text,
    DateTimeOffset CreatedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? DoneAt,
    DateTimeOffset EditedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? DeletedAt,
    double Position) : ISharedItem
{
    [JsonIgnore]
    public string TieBreak => string.Join("\u0001", Text, CreatedAt.UtcTicks.ToString(), DoneAt?.UtcTicks.ToString() ?? string.Empty);

    public ISharedItem WithStamps(DateTimeOffset editedAt, DateTimeOffset? deletedAt)
    {
        return this with { EditedAt = editedAt, DeletedAt = deletedAt };
    }
}
