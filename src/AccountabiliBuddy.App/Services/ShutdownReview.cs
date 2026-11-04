namespace AccountabiliBuddy.App.Services;

/// <summary>
/// "How did today go" from the shutdown routine: a 1 to 5 feeling and a one-line note. Kept sealed on this PC for 90 days
/// (documents "shutdown.reviews"); the note is private text, never logged and not synced (a later round may make that a choice).
/// </summary>
/// <param name="Date">The plan date the review is about.</param>
/// <param name="Feeling">1 (rough) to 5 (great); null when only a note was written.</param>
/// <param name="Note">One line; empty when only a feeling was chosen.</param>
/// <param name="SavedAt">When it was saved.</param>
public sealed record ShutdownReview(DateOnly Date, int? Feeling, string Note, DateTimeOffset SavedAt);
