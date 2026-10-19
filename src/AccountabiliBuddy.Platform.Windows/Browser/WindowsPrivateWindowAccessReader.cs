using System.Text.Json;
using AccountabiliBuddy.Core.Browser;
using AccountabiliBuddy.Core.Ports;

namespace AccountabiliBuddy.Platform.Windows.Browser;

/// <summary>
/// Reads private-window access from the browsers' own settings files, so it works with the browser closed.
/// <list type="bullet">
/// <item>Chrome, Edge, Brave: each profile's "Secure Preferences" (or "Preferences") lists installed extensions under
/// extensions.settings.&lt;id&gt;; "incognito": true means allowed in private windows.</item>
/// <item>Firefox: each profile's extension-preferences.json lists "internal:privateBrowsingAllowed" among the add-on's
/// permissions; extensions.json says whether it's installed.</item>
/// </list>
/// The files are opened read-only with full sharing; these formats are the browsers' internal ones, so anything
/// unexpected reads as <see cref="PrivateWindowAccess.Unknown"/>.
/// </summary>
public sealed class WindowsPrivateWindowAccessReader : IPrivateWindowAccessReader
{
    private const string FirefoxPrivatePermission = "internal:privateBrowsingAllowed";

    private static readonly Dictionary<BrowserKind, string> ChromiumUserData = new()
    {
        [BrowserKind.Chrome] = @"%LOCALAPPDATA%\Google\Chrome\User Data",
        [BrowserKind.Edge] = @"%LOCALAPPDATA%\Microsoft\Edge\User Data",
        [BrowserKind.Brave] = @"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data",
    };

    public PrivateWindowAccess Read(BrowserKind browser)
    {
        try
        {
            if (browser == BrowserKind.Firefox)
            {
                return Combine(FirefoxProfiles().Select(ReadFirefoxProfile));
            }

            return ChromiumUserData.TryGetValue(browser, out string? root)
                ? Combine(ChromiumProfiles(Environment.ExpandEnvironmentVariables(root)).Select(ReadChromiumProfile))
                : PrivateWindowAccess.Unknown;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return PrivateWindowAccess.Unknown;
        }
    }

    /// <summary>Not allowed in any profile wins, then allowed, then unknown; no profile with the extension = not installed.</summary>
    private static PrivateWindowAccess Combine(IEnumerable<PrivateWindowAccess> profiles)
    {
        List<PrivateWindowAccess> all = profiles.ToList();
        if (all.Contains(PrivateWindowAccess.NotAllowed))
        {
            return PrivateWindowAccess.NotAllowed;
        }

        if (all.Contains(PrivateWindowAccess.Allowed))
        {
            return PrivateWindowAccess.Allowed;
        }

        return all.Contains(PrivateWindowAccess.Unknown) ? PrivateWindowAccess.Unknown : PrivateWindowAccess.NotInstalled;
    }

    private static IEnumerable<string> ChromiumProfiles(string root)
    {
        return Directory.Exists(root)
            ? Directory.EnumerateDirectories(root).Where(folder => File.Exists(Path.Combine(folder, "Secure Preferences"))
                                                                   || File.Exists(Path.Combine(folder, "Preferences")))
            : Enumerable.Empty<string>();
    }

    private static PrivateWindowAccess ReadChromiumProfile(string profile)
    {
        PrivateWindowAccess result = PrivateWindowAccess.NotInstalled;
        foreach (string file in new[] { "Secure Preferences", "Preferences" })
        {
            using JsonDocument? preferences = ReadJson(Path.Combine(profile, file));
            if (preferences is null)
            {
                continue;
            }

            if (preferences.RootElement.TryGetProperty("extensions", out JsonElement extensions)
                && extensions.TryGetProperty("settings", out JsonElement settings)
                && settings.TryGetProperty(BrowserBridge.ChromiumExtensionId, out JsonElement extension))
            {
                return extension.TryGetProperty("incognito", out JsonElement incognito) && incognito.ValueKind == JsonValueKind.True
                    ? PrivateWindowAccess.Allowed
                    : PrivateWindowAccess.NotAllowed;
            }
        }

        return result;
    }

    private static IEnumerable<string> FirefoxProfiles()
    {
        string root = Environment.ExpandEnvironmentVariables(@"%APPDATA%\Mozilla\Firefox\Profiles");
        return Directory.Exists(root) ? Directory.EnumerateDirectories(root) : Enumerable.Empty<string>();
    }

    private static PrivateWindowAccess ReadFirefoxProfile(string profile)
    {
        using JsonDocument? installed = ReadJson(Path.Combine(profile, "extensions.json"));
        bool hasExtension = installed is not null
                            && installed.RootElement.TryGetProperty("addons", out JsonElement addons)
                            && addons.ValueKind == JsonValueKind.Array
                            && addons.EnumerateArray().Any(addon => addon.TryGetProperty("id", out JsonElement id)
                                                                    && id.GetString() == BrowserBridge.FirefoxExtensionId);
        if (!hasExtension)
        {
            return installed is null ? PrivateWindowAccess.Unknown : PrivateWindowAccess.NotInstalled;
        }

        using JsonDocument? preferences = ReadJson(Path.Combine(profile, "extension-preferences.json"));
        bool allowed = preferences is not null
                       && preferences.RootElement.TryGetProperty(BrowserBridge.FirefoxExtensionId, out JsonElement extension)
                       && extension.TryGetProperty("permissions", out JsonElement permissions)
                       && permissions.ValueKind == JsonValueKind.Array
                       && permissions.EnumerateArray().Any(permission => permission.GetString() == FirefoxPrivatePermission);
        return allowed ? PrivateWindowAccess.Allowed : PrivateWindowAccess.NotAllowed;
    }

    private static JsonDocument? ReadJson(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonDocument.Parse(stream);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
