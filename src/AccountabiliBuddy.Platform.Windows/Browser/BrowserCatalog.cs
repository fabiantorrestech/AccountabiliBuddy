using AccountabiliBuddy.Core.Ports;
using Microsoft.Win32;

namespace AccountabiliBuddy.Platform.Windows.Browser;

/// <summary>
/// Where each supported browser lives on Windows and where it looks for native messaging hosts.
/// Brave on Windows reads Chrome's registration (verified: it ignores its own BraveSoftware key), so the two share one.
/// </summary>
internal static class BrowserCatalog
{
    private const string ChromeHostKey = @"Software\Google\Chrome\NativeMessagingHosts";

    public static IReadOnlyList<BrowserInfo> All { get; } = new[]
    {
        new BrowserInfo(BrowserKind.Chrome, "Google Chrome", "chrome.exe",
            ChromeHostKey, "chrome://extensions", IsFirefox: false),
        new BrowserInfo(BrowserKind.Brave, "Brave", "brave.exe",
            ChromeHostKey, "brave://extensions", IsFirefox: false),
        new BrowserInfo(BrowserKind.Edge, "Microsoft Edge", "msedge.exe",
            @"Software\Microsoft\Edge\NativeMessagingHosts", "edge://extensions", IsFirefox: false),
        new BrowserInfo(BrowserKind.Firefox, "Mozilla Firefox", "firefox.exe",
            @"Software\Mozilla\NativeMessagingHosts", "about:debugging#/runtime/this-firefox", IsFirefox: true),
    };

    public static BrowserInfo Get(BrowserKind kind)
    {
        return All.FirstOrDefault(browser => browser.Kind == kind)
            ?? throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported browser.");
    }

    /// <summary>Finds the browser's executable through the "App Paths" registration installers create.</summary>
    public static string? FindExecutable(BrowserInfo browser)
    {
        string subKey = $@"Software\Microsoft\Windows\CurrentVersion\App Paths\{browser.ExeName}";
        foreach (RegistryKey root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using RegistryKey? key = root.OpenSubKey(subKey);
            if (key?.GetValue(null) is string path && File.Exists(path.Trim('"')))
            {
                return path.Trim('"');
            }
        }

        return null;
    }
}
