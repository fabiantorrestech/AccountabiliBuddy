using System.Globalization;
using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// An Auto-saved item (ADR 72): "tomorrow's first task" typed on a shutdown card that closed by itself, shared between the
/// user's devices item by item like ADR 48's lists. It goes for good <see cref="Kept"/> after <see cref="SavedAt"/> on every
/// device. A deleted one travels as a tombstone with no text. Only the sealed files carry it; the text is never logged.
/// </summary>
/// <param name="WrapUpOf">The plan date the shutdown card wrapped up.</param>
/// <param name="SavedAt">When the card closed and saved it; the 30 days run from then.</param>
/// <param name="Category">The category's name; null for none.</param>
/// <param name="CategoryColor">The category's colour, "#RRGGBB".</param>
/// <param name="Labels">Its labels; null for none.</param>
/// <param name="LocalTo">The device that keeps it as a local item; null = it syncs.</param>
public sealed record SyncAutoSavedItem(
    Guid Id,
    string Title,
    DateOnly WrapUpOf,
    DateTimeOffset SavedAt,
    DateTimeOffset EditedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? DeletedAt,
    double Position,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Category = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CategoryColor = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Labels = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? LocalTo = null) : ISharedItem
{
    /// <summary>How long an item is kept after it was saved.</summary>
    public static readonly TimeSpan Kept = TimeSpan.FromDays(30);

    [JsonIgnore]
    public string TieBreak => string.Join(
        "\u0001",
        new[]
        {
            Title, WrapUpOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), SavedAt.UtcTicks.ToString(CultureInfo.InvariantCulture),
            Category ?? string.Empty, CategoryColor ?? string.Empty,
        }.Concat(SharedItemTie.Extra(Labels, LocalTo)));

    /// <summary>Whether it is past its 30 days at <paramref name="now"/>.</summary>
    public bool IsExpired(DateTimeOffset now)
    {
        return now - SavedAt >= Kept;
    }

    public ISharedItem WithStamps(DateTimeOffset editedAt, DateTimeOffset? deletedAt)
    {
        return this with { EditedAt = editedAt, DeletedAt = deletedAt };
    }

    public ISharedItem AsLocalStub()
    {
        return Deleted(EditedAt) with { LocalTo = LocalTo };
    }

    /// <summary>The item as a tombstone: its text is gone, and it is deleted at <paramref name="at"/>.</summary>
    public SyncAutoSavedItem Deleted(DateTimeOffset at)
    {
        return this with { Title = string.Empty, Category = null, CategoryColor = null, Labels = null, LocalTo = null, EditedAt = at, DeletedAt = at };
    }
}
