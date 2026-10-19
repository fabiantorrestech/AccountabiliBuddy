using System.Text.Json;
using AccountabiliBuddy.Core.Import;
using AccountabiliBuddy.Core.Ports;

namespace AccountabiliBuddy.Platform.Windows.Integrations;

/// <summary>
/// Finds Cold Turkey's databases (%ProgramData%\Cold Turkey) and blocker extensions in Chromium browsers' profiles
/// (Chrome, Edge, Brave, Vivaldi, Opera). Files are opened with full sharing because their owners keep them open,
/// and copied or read in one go; nothing is ever written there.
/// </summary>
public sealed class WindowsBlockerSourceLocator : IBlockerSourceLocator
{
    private static readonly (string Name, string Path, bool IsProfileFolder)[] BrowserRoots =
    {
        ("Chrome", @"%LOCALAPPDATA%\Google\Chrome\User Data", false),
        ("Edge", @"%LOCALAPPDATA%\Microsoft\Edge\User Data", false),
        ("Brave", @"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data", false),
        ("Vivaldi", @"%LOCALAPPDATA%\Vivaldi\User Data", false),
        ("Opera", @"%APPDATA%\Opera Software\Opera Stable", true),
    };

    // Matched against the extension's name (from its manifest), so store-specific ids don't matter.
    private static readonly (string NamePart, ImportSource Source)[] KnownExtensions =
    {
        ("AppBlock", ImportSource.AppBlock),
        ("BlockSite", ImportSource.BlockSite),
        ("LeechBlock", ImportSource.LeechBlock),
    };

    public ColdTurkeyDataFiles? CopyColdTurkeyData()
    {
        string source = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Cold Turkey");
        string settings = Path.Combine(source, "data-app.db");
        if (!File.Exists(settings))
        {
            return null;
        }

        string folder = Path.Combine(Path.GetTempPath(), "AccountabiliBuddy-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return new ColdTurkeyDataFiles(
            folder,
            CopyShared(settings, folder) ?? throw new IOException("Cold Turkey's settings couldn't be copied."),
            CopyShared(Path.Combine(source, "data-browser.db"), folder),
            CopyShared(Path.Combine(source, "data-helper.db"), folder));
    }

    public IReadOnlyList<BlockerExtensionData> FindBlockerExtensions()
    {
        var found = new List<BlockerExtensionData>();
        foreach ((string browser, string rootPattern, bool isProfileFolder) in BrowserRoots)
        {
            string root = Environment.ExpandEnvironmentVariables(rootPattern);
            if (!Directory.Exists(root))
            {
                continue;
            }

            IReadOnlyDictionary<string, string> profileNames = isProfileFolder ? new Dictionary<string, string>() : ProfileNames(root);
            IEnumerable<string> profiles = isProfileFolder
                ? new[] { root }
                : Directory.EnumerateDirectories(root).Where(folder => Directory.Exists(Path.Combine(folder, "Extensions")));
            foreach (string profile in profiles)
            {
                string folderName = Path.GetFileName(profile);
                string where = isProfileFolder || folderName == "Default"
                    ? browser
                    : $"{browser}, {profileNames.GetValueOrDefault(folderName, folderName)}";
                found.AddRange(ExtensionsIn(profile, where));
            }
        }

        return found;
    }

    private static IEnumerable<BlockerExtensionData> ExtensionsIn(string profile, string where)
    {
        string extensions = Path.Combine(profile, "Extensions");
        if (!Directory.Exists(extensions))
        {
            yield break;
        }

        foreach (string extensionFolder in SafeDirectories(extensions))
        {
            string id = Path.GetFileName(extensionFolder);
            string? versionFolder = SafeDirectories(extensionFolder).OrderByDescending(Directory.GetLastWriteTimeUtc).FirstOrDefault();
            string? name = versionFolder is null ? null : ExtensionName(versionFolder);
            ImportSource? source = name is null ? null : SourceOf(name);
            if (source is null)
            {
                continue;
            }

            string storage = Path.Combine(profile, "Local Extension Settings", id);
            IReadOnlyList<byte[]> logs = Directory.Exists(storage)
                ? Directory.GetFiles(storage, "*.log").OrderBy(path => path, StringComparer.Ordinal)
                    .Select(ReadShared).OfType<byte[]>().ToList()
                : Array.Empty<byte[]>();
            bool hasCompacted = Directory.Exists(storage) && Directory.GetFiles(storage, "*.ldb").Length > 0;
            byte[]? rules = ReadShared(Path.Combine(profile, "DNR Extension Rules", id, "rules.json"));
            string? rulesJson = rules is null ? null : System.Text.Encoding.UTF8.GetString(rules);
            yield return new BlockerExtensionData(source.Value, where, rulesJson, logs, hasCompacted);
        }
    }

    private static ImportSource? SourceOf(string extensionName)
    {
        foreach ((string namePart, ImportSource source) in KnownExtensions)
        {
            if (extensionName.Contains(namePart, StringComparison.OrdinalIgnoreCase))
            {
                return source;
            }
        }

        return null;
    }

    /// <summary>The manifest's name, resolving "__MSG_key__" from the default locale's messages.</summary>
    private static string? ExtensionName(string versionFolder)
    {
        try
        {
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(versionFolder, "manifest.json")));
            string? name = manifest.RootElement.TryGetProperty("name", out JsonElement value) ? value.GetString() : null;
            if (name is null || !name.StartsWith("__MSG_", StringComparison.Ordinal) || !name.EndsWith("__", StringComparison.Ordinal))
            {
                return name;
            }

            string key = name[6..^2];
            string locale = manifest.RootElement.TryGetProperty("default_locale", out JsonElement loc) ? loc.GetString() ?? "en" : "en";
            using JsonDocument messages = JsonDocument.Parse(File.ReadAllText(Path.Combine(versionFolder, "_locales", locale, "messages.json")));
            foreach (JsonProperty message in messages.RootElement.EnumerateObject())
            {
                if (string.Equals(message.Name, key, StringComparison.OrdinalIgnoreCase)
                    && message.Value.TryGetProperty("message", out JsonElement text))
                {
                    return text.GetString();
                }
            }

            return name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Profile folder → the name the user gave it, from the browser's "Local State" file.</summary>
    private static IReadOnlyDictionary<string, string> ProfileNames(string root)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        byte[]? localState = ReadShared(Path.Combine(root, "Local State"));
        if (localState is null)
        {
            return names;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(localState);
            if (document.RootElement.TryGetProperty("profile", out JsonElement profile)
                && profile.TryGetProperty("info_cache", out JsonElement cache))
            {
                foreach (JsonProperty entry in cache.EnumerateObject())
                {
                    if (entry.Value.TryGetProperty("name", out JsonElement name) && name.GetString() is { Length: > 0 } text)
                    {
                        names[entry.Name] = text;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Folder names are used instead.
        }

        return names;
    }

    private static IEnumerable<string> SafeDirectories(string folder)
    {
        try
        {
            return Directory.GetDirectories(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private static byte[]? ReadShared(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? CopyShared(string path, string folder)
    {
        byte[]? bytes = ReadShared(path);
        if (bytes is null)
        {
            return null;
        }

        string target = Path.Combine(folder, Path.GetFileName(path));
        File.WriteAllBytes(target, bytes);
        return target;
    }
}
