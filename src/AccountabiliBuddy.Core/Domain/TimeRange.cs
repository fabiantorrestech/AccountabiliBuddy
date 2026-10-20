namespace AccountabiliBuddy.Core.Domain;

/// <summary>A stretch of absolute time, start included, end excluded.</summary>
public readonly record struct TimeRange(DateTimeOffset From, DateTimeOffset To);
