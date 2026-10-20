namespace AccountabiliBuddy.Core.Domain;

/// <summary>Time spent on one app (process name) or site (domain) in a period.</summary>
/// <param name="Section">For a site with sections (ADR 17), the subreddit, channel or repository; null for the rest of
/// its time.</param>
public sealed record UsageEntry(string Name, TimeSpan Time, string? Section = null);
