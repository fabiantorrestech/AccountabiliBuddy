using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Platform.Windows.Browser;
using AccountabiliBuddy.Platform.Windows.Shortcuts;
using AccountabiliBuddy.Platform.Windows.Startup;

namespace AccountabiliBuddy.App;

/// <summary>
/// Run by the installer just before uninstalling (it must be quick): removes what this installed copy registered,
/// i.e. start with Windows, the Start menu quick-session shortcut and browser connections that point to it.
/// The user's data stays (Setup → "Delete my data" removes it), so a reinstall carries on where it left off.
/// </summary>
public static class UninstallCleanup
{
    public static void Run()
    {
        TryRun(() =>
        {
            var startup = new RunKeyStartupRegistrar(AppPaths.LaunchPath, StartupOptions.MinimizedArgument);
            if (startup.IsEnabled)
            {
                startup.Disable();
            }
        });
        TryRun(() => new StartMenuQuickLaunchShortcut(AppPaths.LaunchPath, StartupOptions.QuickSessionArgument).Remove());
        TryRun(() => new WindowsBrowserSetup(AppPaths.NativeHostPath, AppPaths.BundledExtensionFolder, LocalDatabase.DefaultDirectory())
            .UnregisterWhereHostIs(AppPaths.NativeHostPath));
    }

    private static void TryRun(Action step)
    {
        try
        {
            step();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException
                                       or System.Runtime.InteropServices.COMException)
        {
            // Best effort: uninstalling must not fail because of a leftover registration.
        }
    }
}
