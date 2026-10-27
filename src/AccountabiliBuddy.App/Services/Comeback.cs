namespace AccountabiliBuddy.App.Services;

/// <summary>A thought that must not drift (redesign F/K): listed on Today until ticked.</summary>
/// <param name="DoneAt">When it was ticked; null while open.</param>
/// <param name="EditedAt">When it was last changed, for merging with the user's other devices (ADR 48); filled in when an older store is read.</param>
/// <param name="DeletedAt">Set on a tombstone (ADR 48): the text is gone and the item only tells the other devices it was deleted.</param>
/// <param name="Position">Its place in the list shared with the other devices; lower first.</param>
public sealed record Comeback(
    Guid Id,
    string Text,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DoneAt = null,
    DateTimeOffset EditedAt = default,
    DateTimeOffset? DeletedAt = null,
    double Position = 0);
