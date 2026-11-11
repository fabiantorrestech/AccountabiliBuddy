namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// A long title's suggested split into a short title and its details (spec 05 section 6, Implementation Notes "Title length"): the
/// part from <paramref name="At"/> on is highlighted as the part that moves into the note. A suggestion only; the user can ignore it.
/// </summary>
/// <param name="Head">The title that stays.</param>
/// <param name="Tail">The details that move.</param>
/// <param name="At">Where the break is in the title (the index of the first character of the break).</param>
public sealed record TaskTitleSplit(string Head, string Tail, int At);
