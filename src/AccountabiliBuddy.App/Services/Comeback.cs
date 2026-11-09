namespace AccountabiliBuddy.App.Services;

/// <summary>A thought that must not drift (redesign F/K): listed on Today until ticked.</summary>
/// <param name="DoneAt">When it was ticked; null while open.</param>
/// <param name="EditedAt">When it was last changed, for merging with the user's other devices (ADR 48); filled in when an older store is read.</param>
/// <param name="DeletedAt">Set on a tombstone (ADR 48): the text is gone and the item only tells the other devices it was deleted.</param>
/// <param name="Position">Its place in the list shared with the other devices; lower first.</param>
/// <param name="Category">The category's name; null for none. A come-back carries the name and colour itself, like a Later item.</param>
/// <param name="CategoryColor">The category's colour, "#RRGGBB".</param>
/// <param name="Labels">Its labels (ADR 72); null for none.</param>
/// <param name="LocalTo">The device that keeps it as a local item (ADR 72); null = it syncs.</param>
public sealed record Comeback(
    Guid Id,
    string Text,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DoneAt = null,
    DateTimeOffset EditedAt = default,
    DateTimeOffset? DeletedAt = null,
    double Position = 0,
    string? Category = null,
    string? CategoryColor = null,
    IReadOnlyList<string>? Labels = null,
    Guid? LocalTo = null);
