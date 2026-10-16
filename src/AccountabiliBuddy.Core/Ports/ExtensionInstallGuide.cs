namespace AccountabiliBuddy.Core.Ports;

/// <param name="Folder">Where the extension was copied; the browser loads it from here.</param>
/// <param name="ExtensionsPageUrl">The browser page to load it from, e.g. chrome://extensions.</param>
/// <param name="IsTemporary">True for Firefox: an unsigned add-on is removed when Firefox restarts.</param>
public sealed record ExtensionInstallGuide(
    BrowserKind Browser,
    string Folder,
    string ExtensionsPageUrl,
    bool IsTemporary);
