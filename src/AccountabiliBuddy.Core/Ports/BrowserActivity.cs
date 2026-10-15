namespace AccountabiliBuddy.Core.Ports;

/// <summary>Active tab reported by the browser extension.</summary>
/// <param name="TabId">Opaque id used to redirect the tab when enforcing.</param>
public sealed record BrowserActivity(
    DateTimeOffset Timestamp,
    string BrowserName,
    string TabId,
    string Url,
    string Title);
