namespace AccountabiliBuddy.Core.Ports;

/// <summary>The active tab of one browser window, as reported by the extension.</summary>
/// <param name="TabId">Opaque id, unique across connected browsers; used to redirect the tab when enforcing.</param>
/// <param name="Channel">Channel or author of the page when the extension can tell (e.g. a YouTube video's channel).</param>
public sealed record BrowserActivity(
    string TabId,
    string Title,
    string Url,
    string? Channel);
