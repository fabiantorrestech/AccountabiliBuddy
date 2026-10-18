namespace AccountabiliBuddy.Core.Ports;

/// <summary>One running browser whose extension is connected to the app.</summary>
/// <param name="Browser">Which browser the extension says it runs in.</param>
/// <param name="ActiveTabs">The active tab of each open window of that browser.</param>
/// <param name="PrivateWindowsAllowed">Whether the user allowed the extension in private/incognito windows;
/// null when the extension is too old to say.</param>
public sealed record BrowserConnection(
    string ConnectionId,
    BrowserKind Browser,
    string ExtensionVersion,
    DateTimeOffset ConnectedAt,
    DateTimeOffset LastMessageAt,
    IReadOnlyList<BrowserActivity> ActiveTabs,
    bool? PrivateWindowsAllowed = null);
