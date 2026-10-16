using AccountabiliBuddy.Core.Ports;

namespace AccountabiliBuddy.Platform.Windows.Browser;

/// <param name="NativeHostKey">Registry key under HKCU where the browser looks up native messaging hosts.</param>
internal sealed record BrowserInfo(
    BrowserKind Kind,
    string DisplayName,
    string ExeName,
    string NativeHostKey,
    string ExtensionsPageUrl,
    bool IsFirefox);
