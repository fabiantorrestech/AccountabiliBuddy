namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// Time the user deleted from History for one app or site in one period. It stays on disk (so it can be
/// restored) but no longer counts anywhere. Names match ignoring case.
/// </summary>
/// <param name="App">Process name; with no <paramref name="Site"/>, all of that app's time.</param>
/// <param name="Site">
/// Domain: with an <paramref name="App"/>, that site in that browser only; without one, the name as History shows it
/// (the site, or the app for time with no site).
/// </param>
public sealed record UsageDeletion(string? App, string? Site, DateTimeOffset From, DateTimeOffset To);
