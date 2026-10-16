using AccountabiliBuddy.Core.Domain;
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

    private readonly ProcessNameResolver _processNames = new();
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;

    /// <summary>One snapshot per connected monitor, in monitor enumeration order.</summary>
    public IReadOnlyList<ActivitySnapshot> Scan(DateTimeOffset timestamp)
    {
        List<MonitorEntry> monitors = EnumerateMonitors();
        var topWindowByMonitor = new Dictionary<nint, HWND>();

        PInvoke.EnumWindows((window, _) =>
        {
            if (!IsEligible(window))
            {
                return true;
            }

            HMONITOR monitor = PInvoke.MonitorFromWindow(window, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONULL);
            if (!monitor.IsNull)
            {
                topWindowByMonitor.TryAdd(monitor, window);
            }

            // Stop once every monitor has its top window.
            return topWindowByMonitor.Count < monitors.Count;
        }, default);

        var snapshots = new List<ActivitySnapshot>(monitors.Count);
        foreach (MonitorEntry monitor in monitors)
        {
            snapshots.Add(topWindowByMonitor.TryGetValue(monitor.Handle, out HWND window)
                ? CreateSnapshot(timestamp, monitor.DeviceName, window)
                : ActivitySnapshot.ForEmptyDesktop(timestamp, monitor.DeviceName));
        }

        return snapshots;
    }

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

        return !ShellWindowClasses.Contains(ReadClassName(window));
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
