namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// A stretch of a past session the user marked as distracted in History (start included, end excluded). It comes out
/// of focus totals like time stepped away, and counts as distracted.
/// </summary>
public sealed record SessionGap(DateTimeOffset From, DateTimeOffset To);
