using System.ComponentModel;
using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace AccountabiliBuddy.Platform.Windows.Startup;

/// <summary>
/// The watchdog process itself (ADR 59): waits, at no CPU cost, for the app to let go of its single-instance mutex. If the app ended
/// while the safeguard held, nothing asked it to stand down and Windows isn't shutting down, it starts the app again, at most
/// <see cref="MaxRelaunches"/> times in <see cref="RelaunchWindow"/> so a fault at startup can't trap the PC.
/// </summary>
public static class QuitWatchdogRunner
{
    private const int MaxRelaunches = 3;
    private static readonly TimeSpan RelaunchWindow = TimeSpan.FromMinutes(10);

    // Lets a shutdown or sign-out that is only starting show itself before anything is relaunched.
    private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(2);

    // How long a relaunched app has to say it is up before its mutex is watched again.
    private static readonly TimeSpan StartWait = TimeSpan.FromSeconds(60);

    /// <summary>Runs until there is nothing left to watch. Blocks the calling thread.</summary>
    public static void Run(string instanceName, string launchPath, string launchArguments)
    {
        // Waiting on a mutex together with another handle needs an MTA thread; the app's Main is STA.
        var thread = new Thread(() => Watch(instanceName, launchPath, launchArguments));
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        thread.Join();
    }

    private static void Watch(string instanceName, string launchPath, string launchArguments)
    {
        using var alive = new Mutex(initiallyOwned: true, QuitWatchdogNames.Alive(instanceName), out bool first);
        if (!first)
        {
            return;
        }

        using var app = new Mutex(initiallyOwned: false, QuitWatchdogNames.App(instanceName));
        using var locked = new EventWaitHandle(false, EventResetMode.ManualReset, QuitWatchdogNames.Locked(instanceName));
        using var standDown = new EventWaitHandle(false, EventResetMode.ManualReset, QuitWatchdogNames.StandDown(instanceName));
        using var appStarted = new EventWaitHandle(false, EventResetMode.ManualReset, QuitWatchdogNames.AppStarted(instanceName));
        var relaunches = new Queue<DateTime>();

        while (WaitForAppToEnd(app, standDown))
        {
            // Held only for this moment, so the relaunched app can take it.
            app.ReleaseMutex();
            if (standDown.WaitOne(0) || !locked.WaitOne(0) || IsWindowsShuttingDown())
            {
                return;
            }

            Thread.Sleep(SettleTime);
            if (standDown.WaitOne(0) || IsWindowsShuttingDown())
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            while (relaunches.Count > 0 && now - relaunches.Peek() > RelaunchWindow)
            {
                relaunches.Dequeue();
            }

            if (relaunches.Count >= MaxRelaunches)
            {
                return;
            }

            relaunches.Enqueue(now);
            appStarted.Reset();
            if (!TryStartApp(launchPath, launchArguments))
            {
                return;
            }

            appStarted.WaitOne(StartWait);
        }
    }

    // True once this thread holds the app's mutex (the app ended); false when told to stand down first.
    private static bool WaitForAppToEnd(Mutex app, EventWaitHandle standDown)
    {
        try
        {
            return WaitHandle.WaitAny(new WaitHandle[] { app, standDown }) == 0;
        }
        catch (AbandonedMutexException ex) when (ex.MutexIndex == 0)
        {
            // A killed app never released it; this thread owns it now.
            return true;
        }
    }

    private static bool TryStartApp(string launchPath, string launchArguments)
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo(launchPath, launchArguments) { UseShellExecute = false });
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static bool IsWindowsShuttingDown()
    {
        return PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_SHUTTINGDOWN) != 0;
    }
}
