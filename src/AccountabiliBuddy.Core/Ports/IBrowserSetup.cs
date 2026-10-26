namespace AccountabiliBuddy.Core.Ports;

/// <summary>
/// Lets the user grant or revoke a browser's connection to the app and install the extension.
/// Granting registers the native messaging host for the current user only (no admin rights needed).
/// </summary>
public interface IBrowserSetup
{
    IReadOnlyList<BrowserKind> SupportedBrowsers { get; }

    BrowserSetupStatus GetStatus(BrowserKind browser);

    /// <summary>Registers the native messaging host so the extension in this browser may talk to the app.</summary>
    void Register(BrowserKind browser);

    /// <summary>Removes the registration; the extension can no longer reach the app.</summary>
    void Unregister(BrowserKind browser);

    /// <summary>Copies the extension to a stable folder that the browser can load it from.</summary>
    ExtensionInstallGuide PrepareExtensionInstall(BrowserKind browser);

    /// <summary>
    /// The path the browser loads the extension from, if <see cref="PrepareExtensionInstall"/> already made the copy:
    /// the folder for Chromium browsers, its manifest.json for Firefox. Null when there is no copy yet; copies nothing.
    /// </summary>
    string? FindPreparedExtensionPath(BrowserKind browser);

    /// <summary>Opens the browser on its extensions page (best effort; some browsers refuse internal pages from the command line).</summary>
    void OpenExtensionsPage(BrowserKind browser);

    void OpenFolder(string path);

    /// <summary>
    /// Registers again every browser whose registration points to another copy's host program (e.g. after the app
    /// was installed, updated or run from a build folder), so the extension reaches this copy.
    /// </summary>
    /// <returns>The browsers whose registration was changed.</returns>
    IReadOnlyList<BrowserKind> RepairRegistrations();

    /// <summary>Updates the extension copies made by <see cref="PrepareExtensionInstall"/> when the bundled extension is newer.</summary>
    /// <returns>True if a copy was updated (the browser must reload the extension to use it).</returns>
    bool RefreshExtensionCopies();
}
