namespace AccountabiliBuddy.Core.Domain;

/// <summary>Time in one app, and for a browser on one site in it.</summary>
/// <param name="App">Process name, e.g. "brave".</param>
/// <param name="Site">The site's domain, or null for time in the app outside any known site.</param>
/// <param name="Section">For a site with sections (ADR 17), the subreddit, channel or repository; null for the rest of
/// its time.</param>
public sealed record AppSiteUsage(string App, string? Site, TimeSpan Time, string? Section = null);
