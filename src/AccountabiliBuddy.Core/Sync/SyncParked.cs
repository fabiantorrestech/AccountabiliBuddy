using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// A come-back (ADR 30, 48), the user's parked thought, as the sealed sync files share it between the user's devices. A
/// ticked one is kept with when it was ticked; a deleted one travels as a tombstone with no text. Only the sealed files
/// carry it; the text is never logged.
/// </summary>
/// <param name="DoneAt">When it was ticked; null while it is open.</param>
/// <param name="Category">The category's name; null for none (optional: an app that predates it neither reads nor writes it).</param>
/// <param name="CategoryColor">The category's colour, "#RRGGBB".</param>
public sealed record SyncParked(
    Guid Id,
    string Text,
    DateTimeOffset CreatedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? DoneAt,
    DateTimeOffset EditedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? DeletedAt,
    double Position,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Category = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CategoryColor = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Labels = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? LocalTo = null) : ISharedItem
{
    // The category joins the tie-break only when there is one: a come-back without it breaks ties exactly as before, and a copy
    // that kept its category beats the same come-back passed on by an app that dropped it (the longer text sorts later). The
    // labels and the keeper (ADR 72) follow the same rule.
    [JsonIgnore]
    public string TieBreak => string.Join(
        "\u0001",
        (Category is null && CategoryColor is null
            ? new[] { Text, CreatedAt.UtcTicks.ToString(), DoneAt?.UtcTicks.ToString() ?? string.Empty }
            : new[] { Text, CreatedAt.UtcTicks.ToString(), DoneAt?.UtcTicks.ToString() ?? string.Empty, Category ?? string.Empty, CategoryColor ?? string.Empty })
        .Concat(SharedItemTie.Extra(Labels, LocalTo)));

    public ISharedItem WithStamps(DateTimeOffset editedAt, DateTimeOffset? deletedAt)
    {
        return this with { EditedAt = editedAt, DeletedAt = deletedAt };
    }

    public ISharedItem AsLocalStub()
    {
        return this with { Text = string.Empty, DoneAt = null, Category = null, CategoryColor = null, Labels = null, DeletedAt = EditedAt };
    }
}
