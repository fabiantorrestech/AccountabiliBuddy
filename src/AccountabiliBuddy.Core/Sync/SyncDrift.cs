namespace AccountabiliBuddy.Core.Sync;

/// <summary>A distracting stretch inside a counted session, from the readable columns only (ADR 8, 17).</summary>
/// <param name="App">The app's process name.</param>
/// <param name="Site">The site's domain, when it was a page in a browser.</param>
/// <param name="Section">The site's section (ADR 17), e.g. "r/manybaggers".</param>
public sealed record SyncDrift(DateTimeOffset Start, DateTimeOffset End, string App, string? Site, string? Section);
