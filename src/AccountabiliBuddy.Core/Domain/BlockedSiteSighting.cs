namespace AccountabiliBuddy.Core.Domain;

/// <summary>A site another blocker (Cold Turkey) was seen blocking in one of the user's browsers.</summary>
/// <param name="Site">The blocked host, e.g. "reddit.com".</param>
public sealed record BlockedSiteSighting(string Site, DateTimeOffset FirstSeen, DateTimeOffset LastSeen, int TimesSeen);
