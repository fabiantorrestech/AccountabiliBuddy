namespace AccountabiliBuddy.Core.Domain;

/// <summary>Time spent on one app (process name) or site (domain) in a period.</summary>
public sealed record UsageEntry(string Name, TimeSpan Time);
