using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Platform.Windows.Display;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace AccountabiliBuddy.Platform.Windows.Activity;

/// <summary>
/// Finds the top visible window on every monitor. EnumWindows walks top-level windows
/// in Z-order (front to back), so the first eligible window on each monitor is what the
/// user sees there, whether or not it has keyboard focus.
/// Not thread-safe.
/// </summary>
internal sealed class DesktopScanner
{
    private const int MaxTitleLength = 512;
    private const int MaxClassNameLength = 256;

    // Desktop, taskbar and other shell surfaces that are never "what the user is looking at".
    private static readonly HashSet<string> ShellWindowClasses = new(StringComparer.Ordinal)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
    };

    // Windows' app switchers (Alt+Tab, Task view). They show other apps' windows as thumbnails and are never
    // what the user is looking at; while one is in front the user hasn't picked a window yet.
    private static readonly HashSet<string> SwitcherWindowClasses = new(StringComparer.Ordinal)
    {
        // Windows 10: Alt+Tab and Task view (Win+Tab), hosted by explorer.
        "MultitaskingViewFrame",
        // Windows 11: Alt+Tab, Task view and Snap Assist, XAML islands hosted by explorer.
        "XamlExplorerHostIslandWindow",
        // Explorer's window that holds the foreground for a moment while a switcher hands it to the chosen app.
        "ForegroundStaging",
        // The classic Alt+Tab grid (AltTabSettings = 1, or no DWM), hosted by explorer.
        "TaskSwitcherWnd",
        // The classic Alt+Tab's overlay that draws over the grid (Windows 7 and later), hosted by explorer.
        "TaskSwitcherOverlayWnd",
    };

    private readonly ProcessNameResolver _processNames = new();
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;

    /// <summary>
    /// True while one of Windows' app switchers (Alt+Tab, Task view) has the foreground: the user is passing over
    /// windows, not looking at one. Checked by class and by a shell process, so an app's window with the same class
    /// name never counts.
    /// </summary>
    public bool IsSwitcherInFront()
    {
        HWND foreground = PInvoke.GetForegroundWindow();
        if (foreground.IsNull || !SwitcherWindowClasses.Contains(ReadClassName(foreground)))
        {
            return false;
        }

        PInvoke.GetWindowThreadProcessId(foreground, out uint processId);
        bool inFront = Win32OverlayWindowControl.ShellProcesses.Contains(_processNames.Resolve(foreground, processId));
#if DEBUG
        // DIAG (Task 4, remove)
        if (inFront)
        {
            LastDiagScan = $"skipped: switcher in front fg={DiagDescribe(foreground)}";
        }
#endif
        return inFront;
    }

    /// <summary>One snapshot per connected monitor, in monitor enumeration order.</summary>
    public IReadOnlyList<ActivitySnapshot> Scan(DateTimeOffset timestamp)
    {
        List<MonitorEntry> monitors = EnumerateMonitors();
        var topWindowByMonitor = new Dictionary<nint, HWND>();
#if DEBUG
        // DIAG (Task 4, remove): the first windows of the walk, eligible or not. No titles.
        var diagWalk = new List<string>(DiagWalkLength);
#endif

        PInvoke.EnumWindows((window, _) =>
        {
            bool eligible = IsEligible(window);
#if DEBUG
            // DIAG (Task 4, remove)
            if (diagWalk.Count < DiagWalkLength)
            {
                diagWalk.Add($"{DiagDescribe(window)}:{(eligible ? "yes" : "no")}");
            }
#endif
            if (!eligible)
            {
#if DEBUG
                // DIAG (Task 4, remove): keep walking until the diag walk is full.
                return topWindowByMonitor.Count < monitors.Count || diagWalk.Count < DiagWalkLength;
#else
                return true;
#endif
            }

            HMONITOR monitor = PInvoke.MonitorFromWindow(window, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONULL);
            if (!monitor.IsNull)
            {
                topWindowByMonitor.TryAdd(monitor, window);
            }

            // Stop once every monitor has its top window.
#if DEBUG
            // DIAG (Task 4, remove): keep walking until the diag walk is full (TryAdd keeps the first top window).
            return topWindowByMonitor.Count < monitors.Count || diagWalk.Count < DiagWalkLength;
#else
            return topWindowByMonitor.Count < monitors.Count;
#endif
        }, default);

#if DEBUG
        // DIAG (Task 4, remove): foreground window, each monitor's chosen top window, the walk.
        var diagTops = new List<string>(monitors.Count);
        foreach (MonitorEntry monitor in monitors)
        {
            diagTops.Add(topWindowByMonitor.TryGetValue(monitor.Handle, out HWND top)
                ? $"{monitor.DeviceName}={WindowIds.Format(top)}/{DiagDescribe(top)}"
                : $"{monitor.DeviceName}=empty");
        }

        HWND foreground = PInvoke.GetForegroundWindow();
        string diagForeground = foreground.IsNull ? "none" : DiagDescribe(foreground);
        LastDiagScan = $"fg={diagForeground} | tops: {string.Join(", ", diagTops)} | walk: {string.Join(", ", diagWalk)}";
#endif

        var snapshots = new List<ActivitySnapshot>(monitors.Count);
        foreach (MonitorEntry monitor in monitors)
        {
            snapshots.Add(topWindowByMonitor.TryGetValue(monitor.Handle, out HWND window)
                ? CreateSnapshot(timestamp, monitor.DeviceName, window)
                : ActivitySnapshot.ForEmptyDesktop(timestamp, monitor.DeviceName));
        }

        return snapshots;
    }

#if DEBUG
    // DIAG (Task 4, remove)
    private const int DiagWalkLength = 6;

    /// <summary>DIAG (Task 4, remove): what the last scan saw, without titles.</summary>
    public string? LastDiagScan { get; private set; }

    // DIAG (Task 4, remove): "class/process", never the title.
    private string DiagDescribe(HWND window)
    {
        PInvoke.GetWindowThreadProcessId(window, out uint processId);
        return $"{ReadClassName(window)}/{_processNames.Resolve(window, processId)}";
    }
#endif

    private ActivitySnapshot CreateSnapshot(DateTimeOffset timestamp, string monitorId, HWND window)
    {
        PInvoke.GetWindowThreadProcessId(window, out uint processId);
        string processName = _processNames.Resolve(window, processId);

        return new ActivitySnapshot(
            timestamp,
            monitorId,
            WindowIds.Format(window),
            processName,
            ReadWindowTitle(window));
    }

    private bool IsEligible(HWND window)
    {
        if (!PInvoke.IsWindowVisible(window) || PInvoke.IsIconic(window) || IsCloaked(window))
        {
            return false;
        }

        PInvoke.GetWindowThreadProcessId(window, out uint processId);
        if (processId == _ownProcessId)
        {
            return false;
        }

        var exStyle = (WINDOW_EX_STYLE)PInvoke.GetWindowLong(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        bool isToolWindow = exStyle.HasFlag(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW)
            && !exStyle.HasFlag(WINDOW_EX_STYLE.WS_EX_APPWINDOW);
        // Click-through windows are overlays (game/recording HUDs), not content.
        bool isClickThrough = exStyle.HasFlag(WINDOW_EX_STYLE.WS_EX_TRANSPARENT);
        if (isToolWindow || isClickThrough)
        {
            return false;
        }

        if (!PInvoke.GetWindowRect(window, out RECT bounds) || bounds.right <= bounds.left || bounds.bottom <= bounds.top)
        {
            return false;
        }

        string className = ReadClassName(window);
        return !ShellWindowClasses.Contains(className) && !SwitcherWindowClasses.Contains(className);
    }

    /// <summary>Cloaked windows exist but are not shown, e.g. on another virtual desktop or suspended Store apps.</summary>
    private static unsafe bool IsCloaked(HWND window)
    {
        int cloaked = 0;
        HRESULT result = PInvoke.DwmGetWindowAttribute(window, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, &cloaked, sizeof(int));
        return result.Succeeded && cloaked != 0;
    }

    private static string ReadWindowTitle(HWND window)
    {
        Span<char> buffer = stackalloc char[MaxTitleLength];
        int length = PInvoke.GetWindowText(window, buffer);
        return buffer[..length].ToString();
    }

    private static string ReadClassName(HWND window)
    {
        Span<char> buffer = stackalloc char[MaxClassNameLength];
        int length = PInvoke.GetClassName(window, buffer);
        return buffer[..length].ToString();
    }

    private static unsafe List<MonitorEntry> EnumerateMonitors()
    {
        var monitors = new List<MonitorEntry>();

        PInvoke.EnumDisplayMonitors(default, (RECT?)null, (monitor, _, _, _) =>
        {
            var info = new MONITORINFOEXW();
            info.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);

            string deviceName = PInvoke.GetMonitorInfo(monitor, (MONITORINFO*)&info)
                ? info.szDevice.ToString()
                : ((nint)monitor).ToString("X");

            monitors.Add(new MonitorEntry(monitor, deviceName));
            return true;
        }, default);

        return monitors;
    }

    /// <param name="DeviceName">Stable display name such as "\\.\DISPLAY1", used as the monitor id.</param>
    private sealed record MonitorEntry(nint Handle, string DeviceName);
}
