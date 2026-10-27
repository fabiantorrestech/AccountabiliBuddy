namespace AccountabiliBuddy.App;

/// <summary>
/// Where the app's own files are. Installed with Velopack, the real files live in "...\AccountabiliBuddyApp\current"
/// (replaced on every update) and a small launcher sits one folder up; registrations that outlive
/// an update (start with Windows, Start menu shortcut) point to that launcher.
/// </summary>
public static class AppPaths
{
    private const string VelopackCurrentFolder = "current";
    private const string VelopackUpdater = "Update.exe";

    /// <summary>The program to register for launching: Velopack's stable launcher if installed, else this exe.</summary>
    public static string LaunchPath
    {
        get
        {
            string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path is unknown.");
            string? folder = Path.GetDirectoryName(executable);
            if (folder is null || !string.Equals(Path.GetFileName(folder), VelopackCurrentFolder, StringComparison.OrdinalIgnoreCase))
            {
                return executable;
            }

            // Velopack names the launcher after the package title (e.g. "AccountabiliBuddy.exe"); its only other exe
            // in the install root is Update.exe.
            string[] launchers = Directory.GetFiles(Path.GetDirectoryName(folder)!, "*.exe")
                .Where(path => !string.Equals(Path.GetFileName(path), VelopackUpdater, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return launchers.Length == 1 ? launchers[0] : executable;
        }
    }

    /// <summary>
    /// True when running from a Velopack install (installer or portable zip): "...\current\" with Update.exe one folder up.
    /// </summary>
    public static bool IsInstalled
    {
        get
        {
            string? folder = Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty);
            string? root = folder is null ? null : Path.GetDirectoryName(folder);
            return folder is not null && root is not null
                && string.Equals(Path.GetFileName(folder), VelopackCurrentFolder, StringComparison.OrdinalIgnoreCase)
                && File.Exists(Path.Combine(root, VelopackUpdater));
        }
    }

    public static string NativeHostPath => Path.Combine(AppContext.BaseDirectory, "AccountabiliBuddy.NativeHost.exe");

    /// <summary>
    /// The sealed relay program (ADR 46) that ships with the app, in a "relay" folder next to it. A packaged app has the
    /// self-contained build (scripts/package.ps1); a development build has the project's build output, copied there by the App project.
    /// </summary>
    public static string RelayPath => Path.Combine(AppContext.BaseDirectory, "relay", "AccountabiliBuddy.Relay.exe");

    public static string BundledExtensionFolder => Path.Combine(AppContext.BaseDirectory, "browser-extension");
}
