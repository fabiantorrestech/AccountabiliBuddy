using AccountabiliBuddy.Platform.Windows.Shortcuts;
using Avalonia;
using Velopack;

namespace AccountabiliBuddy.App;

public static class Program
{
    public const string InstanceName = "AccountabiliBuddy";

    // After restoring a backup the app restarts itself; the new copy waits this long for the old one to exit.
    private static readonly TimeSpan RestartWait = TimeSpan.FromSeconds(20);

    [STAThread]
    public static void Main(string[] args)
    {
        // Installer hooks first (ADR 10): during install, update or uninstall Velopack runs the app with special
        // arguments, and this handles them and exits.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => UninstallCleanup.Run())
            .Run();

        // Times are always 12-hour with AM/PM, whatever the Windows region's clock (project convention).
        AccountabiliBuddy.App.Services.TwelveHourClock.Apply();

        StartupOptions options = StartupOptions.Parse(args);

        using SingleInstanceGuard? guard = options.Restarted
            ? SingleInstanceGuard.TryAcquire(InstanceName, RestartWait)
            : SingleInstanceGuard.TryAcquire(InstanceName);
        if (guard is null)
        {
            // Already running (lives in the tray): hand over to it. This launch was a user action, so it may
            // pass on the right to bring a window to the front.
            ForegroundHandOff.AllowAnyProcess();
            InstanceSignals.Send(InstanceName, options.QuickSession ? InstanceSignal.QuickSession : InstanceSignal.Show);
            return;
        }

        BuildAvaloniaApp(options).StartWithClassicDesktopLifetime(args);
    }

    // Parameterless overload is used by the Avalonia XAML previewer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return BuildAvaloniaApp(new StartupOptions(StartMinimized: false));
    }

    private static AppBuilder BuildAvaloniaApp(StartupOptions options)
    {
        return AppBuilder.Configure(() => new App(options))
            .UsePlatformDetect()
            .LogToTrace();
    }
}
