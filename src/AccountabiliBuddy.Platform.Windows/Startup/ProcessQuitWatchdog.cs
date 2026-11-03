using System.ComponentModel;
using System.Diagnostics;
using AccountabiliBuddy.Core.Ports;

namespace AccountabiliBuddy.Platform.Windows.Startup;

/// <summary>
/// The app's side of the quit watchdog (ADR 59): starts the hidden second copy of the app (<see cref="QuitWatchdogRunner"/>) and tells it
/// through named events whether the safeguard holds. Use from the UI thread only.
/// </summary>
public sealed class ProcessQuitWatchdog : IQuitWatchdog, IDisposable
{
    private readonly string _instanceName;
    private readonly string _executablePath;
    private readonly string _watchdogArgument;
    private readonly EventWaitHandle _locked;
    private readonly EventWaitHandle _standDown;
    private readonly EventWaitHandle _appStarted;

    public ProcessQuitWatchdog(string instanceName, string executablePath, string watchdogArgument)
    {
        _instanceName = instanceName;
        _executablePath = executablePath;
        _watchdogArgument = watchdogArgument;
        _locked = new EventWaitHandle(false, EventResetMode.ManualReset, QuitWatchdogNames.Locked(instanceName));
        _standDown = new EventWaitHandle(false, EventResetMode.ManualReset, QuitWatchdogNames.StandDown(instanceName));
        _appStarted = new EventWaitHandle(false, EventResetMode.ManualReset, QuitWatchdogNames.AppStarted(instanceName));
    }

    public void Keep(bool locked)
    {
        _standDown.Reset();
        if (locked)
        {
            _locked.Set();
        }
        else
        {
            _locked.Reset();
        }

        _appStarted.Set();
        if (!IsRunning())
        {
            StartProcess();
        }
    }

    public void StandDown()
    {
        _locked.Reset();
        _standDown.Set();
    }

    /// <summary>For a process that isn't the app (the uninstaller's hook): tells a running watchdog to exit. Does nothing when none runs.</summary>
    public static void SignalStandDown(string instanceName)
    {
        if (EventWaitHandle.TryOpenExisting(QuitWatchdogNames.StandDown(instanceName), out EventWaitHandle? standDown))
        {
            using (standDown)
            {
                standDown.Set();
            }
        }
    }

    public void Dispose()
    {
        _locked.Dispose();
        _standDown.Dispose();
        _appStarted.Dispose();
    }

    private bool IsRunning()
    {
        if (!Mutex.TryOpenExisting(QuitWatchdogNames.Alive(_instanceName), out Mutex? alive))
        {
            return false;
        }

        alive.Dispose();
        return true;
    }

    private void StartProcess()
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo(_executablePath, _watchdogArgument) { UseShellExecute = false, CreateNoWindow = true });
        }
        catch (Win32Exception)
        {
            // It is tried again at the next refresh; the Quit lock works without it.
        }
    }
}
