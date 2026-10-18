namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// What is visible on one monitor at one moment.
/// <see cref="MonitorId"/> and <see cref="WindowId"/> are opaque platform identifiers.
/// An empty <see cref="WindowId"/> means no window is visible (only the desktop).
/// <see cref="Url"/>, <see cref="TabId"/> and <see cref="Channel"/> are filled in for browser windows
/// whose active tab the extension reported. <see cref="IsUnseenBrowserWindow"/> marks a window of a watched browser
/// that the extension did not report (see <see cref="Engine.UnseenBrowserWindow"/>).
/// </summary>
public sealed record ActivitySnapshot(
    DateTimeOffset Timestamp,
    string MonitorId,
    string WindowId,
    string ProcessName,
    string WindowTitle,
    string? Url = null,
    string? OcrText = null,
    string? TabId = null,
    string? Channel = null,
    bool IsUnseenBrowserWindow = false)
{
    private static readonly string[] ExtensionPageSchemes = { "chrome-extension://", "moz-extension://", "extension://" };

    public bool IsEmptyDesktop => WindowId.Length == 0;

    /// <summary>
    /// A page that belongs to a browser extension, such as Cold Turkey's (or our own) block page.
    /// Never treated as distracting, so two blockers never fight over the same tab.
    /// </summary>
    public bool IsBrowserExtensionPage => Url is not null
        && ExtensionPageSchemes.Any(scheme => Url.StartsWith(scheme, StringComparison.OrdinalIgnoreCase));

    public static ActivitySnapshot ForEmptyDesktop(DateTimeOffset timestamp, string monitorId)
    {
        return new ActivitySnapshot(timestamp, monitorId, string.Empty, string.Empty, string.Empty);
    }
}
