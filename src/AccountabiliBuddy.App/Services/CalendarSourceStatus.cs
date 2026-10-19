namespace AccountabiliBuddy.App.Services;

/// <summary>How fetching one connected source went.</summary>
/// <param name="LastAttemptAt">Last fetch attempt, successful or not.</param>
/// <param name="LastSuccessAt">Last successful fetch (also the "changed since" point for Google).</param>
/// <param name="LastFullFetchAt">Last time everything was fetched rather than only changes.</param>
/// <param name="Error">Why the last attempt failed; null when it worked.</param>
/// <param name="ETag">iCal links: the server's version tag, so unchanged files are skipped.</param>
/// <param name="LastModified">iCal links: the server's last-modified date, for the same purpose.</param>
public sealed record CalendarSourceStatus(
    DateTimeOffset? LastAttemptAt = null,
    DateTimeOffset? LastSuccessAt = null,
    DateTimeOffset? LastFullFetchAt = null,
    string? Error = null,
    string? ETag = null,
    string? LastModified = null);
