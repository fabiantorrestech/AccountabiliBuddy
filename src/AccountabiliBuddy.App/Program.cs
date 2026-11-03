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
        // The quit safeguard's watchdog runs this too (ADR 59); it must never apply a downloaded update and end the app mid-session.
        VelopackApp.Build()
            .SetAutoApplyOnStartup(!args.Contains(StartupOptions.WatchdogArgument))
            .OnBeforeUninstallFastCallback(_ => UninstallCleanup.Run())
            .Run();

        // Times are always 12-hour with AM/PM, whatever the Windows region's clock (project convention).
        AccountabiliBuddy.App.Services.TwelveHourClock.Apply();

        StartupOptions options = StartupOptions.Parse(args);

        // Before the single-instance check and Avalonia: the watchdog only waits for the app to end (ADR 59).
        if (options.Watchdog)
        {
            AccountabiliBuddy.Platform.Windows.Startup.QuitWatchdogRunner.Run(InstanceName, AppPaths.LaunchPath, StartupOptions.MinimizedArgument);
            return;
        }

        SingleInstanceGuard? guard = options.Restarted
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

        try
        {
            BuildAvaloniaApp(options).StartWithClassicDesktopLifetime(args);
            if (App.WindowsShutdownRequested)
            {
                // The mutex is held through the wait so no second copy starts meanwhile; the new copy below waits for it (--restarted).
                bool relaunch = ShutdownWasCancelled();
                guard.Dispose();
                guard = null;
                if (relaunch)
                {
                    RelaunchQuietly();
                }
            }
        }
        finally
        {
            guard?.Dispose();
        }
    }

    // With no window left to cancel the close, Avalonia exits the app as soon as Windows asks it to shut down (WM_QUERYENDSESSION). If the
    // user then cancels the shutdown (an unsaved Notepad, "Cancel" on the blocking screen), nothing is left running. So the process waits:
    // Windows ends it if the shutdown is real; if it is still alive after 30 s with no shutdown going on, it was cancelled.
    private static bool ShutdownWasCancelled()
    {
        Thread.Sleep(CancelledShutdownWait);
        DateTime giveUpAt = DateTime.UtcNow + ShutdownGiveUp;
        while (GetSystemMetrics(SmShuttingDown) != 0)
        {
            if (DateTime.UtcNow >= giveUpAt)
            {
                return false;
            }

            Thread.Sleep(TimeSpan.FromSeconds(5));
        }

        return true;
    }

    // ADR 60: the session comes back through the gap policy (a gap under 2 minutes carries on), and the next sync file has no OffSince.
    private static void RelaunchQuietly()
    {
        if (Environment.ProcessPath is not string executable)
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(executable, $"{StartupOptions.RestartedArgument} {StartupOptions.MinimizedArgument}") { UseShellExecute = false });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Nothing more can be done from here; the next sign-in starts the app.
        }
    }

    private const int SmShuttingDown = 0x2000;
    private static readonly TimeSpan CancelledShutdownWait = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ShutdownGiveUp = TimeSpan.FromMinutes(3);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

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
