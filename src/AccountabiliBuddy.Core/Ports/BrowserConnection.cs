namespace AccountabiliBuddy.Core.Ports;

/// <summary>One running browser whose extension is connected to the app.</summary>
/// <param name="Browser">Which browser the extension says it runs in.</param>
/// <param name="ActiveTabs">The active tab of each open window of that browser.</param>
public sealed record BrowserConnection(
    string ConnectionId,
    BrowserKind Browser,
    string ExtensionVersion,
    DateTimeOffset ConnectedAt,
    DateTimeOffset LastMessageAt,
    IReadOnlyList<BrowserActivity> ActiveTabs);
