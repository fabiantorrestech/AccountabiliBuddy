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

    /// <summary>Opens the browser on its extensions page (best effort; some browsers refuse internal pages from the command line).</summary>
    void OpenExtensionsPage(BrowserKind browser);

    void OpenFolder(string path);
}
