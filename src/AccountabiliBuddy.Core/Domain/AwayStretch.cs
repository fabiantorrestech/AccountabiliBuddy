namespace AccountabiliBuddy.Core.Domain;

/// <summary>A stretch the user stepped away during a session, start included, end excluded, and why.</summary>
public readonly record struct AwayStretch(DateTimeOffset From, DateTimeOffset To, AwayKind Kind);
