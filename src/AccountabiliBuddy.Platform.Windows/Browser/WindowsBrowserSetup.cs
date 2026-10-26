using System.Diagnostics;
using System.Text.Json;
using AccountabiliBuddy.Core.Browser;
using AccountabiliBuddy.Core.Ports;
using Microsoft.Win32;

namespace AccountabiliBuddy.Platform.Windows.Browser;

/// <summary>
/// Registers the native messaging host per browser under HKCU (current user only, no admin rights) and
/// prepares the unpacked extension in a stable folder the browser can keep loading it from.
/// </summary>
public sealed class WindowsBrowserSetup : IBrowserSetup
{
    private readonly string _hostExecutablePath;
    private readonly string _bundledExtensionFolder;
    private readonly string _dataFolder;

    /// <param name="hostExecutablePath">AccountabiliBuddy.NativeHost.exe, shipped next to the app.</param>
    /// <param name="bundledExtensionFolder">The extension files shipped with the app (one subfolder per manifest flavor).</param>
    /// <param name="dataFolder">The app's per-user data folder; manifests and the extension copy go here.</param>
    public WindowsBrowserSetup(string hostExecutablePath, string bundledExtensionFolder, string dataFolder)
    {
        _hostExecutablePath = hostExecutablePath;
        _bundledExtensionFolder = bundledExtensionFolder;
        _dataFolder = dataFolder;
    }

    public IReadOnlyList<BrowserKind> SupportedBrowsers { get; } =
        BrowserCatalog.All.Select(browser => browser.Kind).ToList();

    public BrowserSetupStatus GetStatus(BrowserKind kind)
    {
        BrowserInfo browser = BrowserCatalog.Get(kind);
        bool installed = BrowserCatalog.FindExecutable(browser) is not null;

        using RegistryKey? key = Registry.CurrentUser.OpenSubKey($@"{browser.NativeHostKey}\{BrowserBridge.HostName}");
        string? sharedWith = BrowserCatalog.All
            .Where(other => other.Kind != kind && other.NativeHostKey == browser.NativeHostKey)
            .Select(other => other.DisplayName)
            .FirstOrDefault();

        if (key?.GetValue(null) is not string manifestPath)
        {
            return new BrowserSetupStatus(kind, browser.DisplayName, installed, IsHostRegistered: false, Problem: null, sharedWith);
        }

        string? problem = FindRegistrationProblem(manifestPath);
        return new BrowserSetupStatus(kind, browser.DisplayName, installed, problem is null, problem, sharedWith);
    }

    public void Register(BrowserKind kind)
    {
        BrowserInfo browser = BrowserCatalog.Get(kind);
        string manifestPath = WriteHostManifest(browser.IsFirefox);

        using RegistryKey key = Registry.CurrentUser.CreateSubKey($@"{browser.NativeHostKey}\{BrowserBridge.HostName}");
        key.SetValue(null, manifestPath);
    }

    public void Unregister(BrowserKind kind)
    {
        BrowserInfo browser = BrowserCatalog.Get(kind);
        Registry.CurrentUser.DeleteSubKeyTree($@"{browser.NativeHostKey}\{BrowserBridge.HostName}", throwOnMissingSubKey: false);
    }

    public ExtensionInstallGuide PrepareExtensionInstall(BrowserKind kind)
    {
        BrowserInfo browser = BrowserCatalog.Get(kind);
        string flavor = FlavorOf(browser);
        string source = Path.Combine(_bundledExtensionFolder, flavor);
        string target = Path.Combine(_dataFolder, "browser-extension", flavor);

        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException($"The bundled extension was not found at {source}.");
        }

        CopyFolder(source, target);
        return new ExtensionInstallGuide(kind, target, browser.ExtensionsPageUrl, IsTemporary: browser.IsFirefox);
    }

    public string? FindPreparedExtensionPath(BrowserKind kind)
    {
        BrowserInfo browser = BrowserCatalog.Get(kind);
        string folder = Path.Combine(_dataFolder, "browser-extension", FlavorOf(browser));
        string manifest = Path.Combine(folder, "manifest.json");
        if (!File.Exists(manifest))
        {
            return null;
        }

        // Firefox's "Load Temporary Add-on…" picks the manifest file; Chromium's "Load unpacked" picks the folder.
        return browser.IsFirefox ? manifest : folder;
    }

    public void OpenExtensionsPage(BrowserKind kind)
    {
        BrowserInfo browser = BrowserCatalog.Get(kind);
        string? executable = BrowserCatalog.FindExecutable(browser);
        if (executable is not null)
        {
            Process.Start(new ProcessStartInfo(executable, browser.ExtensionsPageUrl) { UseShellExecute = false })?.Dispose();
        }
    }

    public void OpenFolder(string path)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = false })?.Dispose();
    }

    public IReadOnlyList<BrowserKind> RepairRegistrations()
    {
        var repaired = new List<BrowserKind>();
        foreach (BrowserKind kind in SupportedBrowsers)
        {
            BrowserInfo browser = BrowserCatalog.Get(kind);
            string? manifestPath;
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey($@"{browser.NativeHostKey}\{BrowserBridge.HostName}"))
            {
                manifestPath = key?.GetValue(null) as string;
            }

            if (manifestPath is not null
                && !string.Equals(RegisteredHostPath(manifestPath), _hostExecutablePath, StringComparison.OrdinalIgnoreCase))
            {
                Register(kind);
                repaired.Add(kind);
            }
        }

        return repaired;
    }

    /// <summary>Removes the registrations that point to <paramref name="hostExecutablePath"/> (used when uninstalling that copy).</summary>
    public void UnregisterWhereHostIs(string hostExecutablePath)
    {
        foreach (BrowserKind kind in SupportedBrowsers)
        {
            BrowserInfo browser = BrowserCatalog.Get(kind);
            string? manifestPath;
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey($@"{browser.NativeHostKey}\{BrowserBridge.HostName}"))
            {
                manifestPath = key?.GetValue(null) as string;
            }

            if (manifestPath is not null
                && string.Equals(RegisteredHostPath(manifestPath), hostExecutablePath, StringComparison.OrdinalIgnoreCase))
            {
                Unregister(kind);
            }
        }
    }

    public bool RefreshExtensionCopies()
    {
        bool updated = false;
        foreach (string flavor in new[] { "chromium", "firefox" })
        {
            string source = Path.Combine(_bundledExtensionFolder, flavor);
            string target = Path.Combine(_dataFolder, "browser-extension", flavor);
            if (Directory.Exists(source) && Directory.Exists(target) && ExtensionVersion(source) > ExtensionVersion(target))
            {
                CopyFolder(source, target);
                updated = true;
            }
        }

        return updated;
    }

    private static string? RegisteredHostPath(string manifestPath)
    {
        try
        {
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            return manifest.RootElement.TryGetProperty("path", out JsonElement value) ? value.GetString() : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static Version ExtensionVersion(string folder)
    {
        try
        {
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")));
            return manifest.RootElement.TryGetProperty("version", out JsonElement value) && Version.TryParse(value.GetString(), out Version? version)
                ? version
                : new Version(0, 0);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new Version(0, 0);
        }
    }

    private string WriteHostManifest(bool forFirefox)
    {
        string folder = Path.Combine(_dataFolder, "native-host");
        Directory.CreateDirectory(folder);

        // Chromium browsers identify the extension by origin; Firefox by add-on id.
        object manifest = forFirefox
            ? new
            {
                name = BrowserBridge.HostName,
                description = "AccountabiliBuddy browser bridge",
                path = _hostExecutablePath,
                type = "stdio",
                allowed_extensions = new[] { BrowserBridge.FirefoxExtensionId },
            }
            : new
            {
                name = BrowserBridge.HostName,
                description = "AccountabiliBuddy browser bridge",
                path = _hostExecutablePath,
                type = "stdio",
                allowed_origins = new[] { $"chrome-extension://{BrowserBridge.ChromiumExtensionId}/" },
            };

        string path = Path.Combine(folder, $"{BrowserBridge.HostName}.{(forFirefox ? "firefox" : "chromium")}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    private static string? FindRegistrationProblem(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            return "The registration points to a manifest that no longer exists. Grant again to repair it.";
        }

        try
        {
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            string? hostPath = manifest.RootElement.TryGetProperty("path", out JsonElement value) ? value.GetString() : null;
            return hostPath is not null && File.Exists(hostPath)
                ? null
                : "The registered host program was moved or deleted (e.g. a different build folder). Grant again to repair it.";
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return "The host manifest could not be read. Grant again to repair it.";
        }
    }

    private static string FlavorOf(BrowserInfo browser)
    {
        return browser.IsFirefox ? "firefox" : "chromium";
    }

    private static void CopyFolder(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }
}
