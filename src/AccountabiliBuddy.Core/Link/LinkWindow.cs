namespace AccountabiliBuddy.Core.Link;

/// <summary>A stretch of time, e.g. a drift mark.</summary>
public sealed record LinkWindow(DateTimeOffset Start, DateTimeOffset End);
