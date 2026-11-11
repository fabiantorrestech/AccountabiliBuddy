namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// A project marker the user taught (Implementation Notes "Learning custom markers"): text between <paramref name="Open"/> and
/// <paramref name="Close"/> at the start of a title ("⟦Name⟧"), or, with an empty <paramref name="Open"/>, a word followed by
/// <paramref name="Close"/> ("Name ›").
/// </summary>
public sealed record ProjectMarkerPattern(string Open, string Close);
