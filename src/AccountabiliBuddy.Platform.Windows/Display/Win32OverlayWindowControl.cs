using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace AccountabiliBuddy.Platform.Windows.Display;

/// <summary>
/// Click-through via the window's extended style: WS_EX_TRANSPARENT (with WS_EX_LAYERED) passes the mouse to the
/// window underneath, WS_EX_NOACTIVATE keeps a click from taking focus, WS_EX_TOOLWINDOW keeps it out of Alt+Tab.
/// The drag keys are read with GetAsyncKeyState, which sees them whichever app is in front (no keyboard hook).
/// A fullscreen app is a foreground window without a title bar whose rectangle covers its monitor. A maximized window
/// keeps its title bar, so it never counts (it can cover the monitor too when the taskbar hides itself).
/// </summary>
public sealed class Win32OverlayWindowControl : IOverlayWindowControl
{
    private const ushort KeyDownBit = 0x8000;

    // The desktop and the taskbar cover the screen too but aren't fullscreen apps.
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal) { "Progman", "WorkerW", "Shell_TrayWnd" };

    // Windows' own screens that cover everything for a moment while switching apps (Alt+Tab, Task view, Start,
    // Search, notifications); they are never a fullscreen app.
    private static readonly HashSet<string> ShellProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchApp", "ShellHost",
    };

    private readonly uint _ownProcessId = (uint)Environment.ProcessId;

    public void SetClickThrough(nint windowHandle, bool clickThrough)
    {
        var window = new HWND(windowHandle);
        var style = (WINDOW_EX_STYLE)PInvoke.GetWindowLong(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        style |= WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW;
        style = clickThrough ? style | WINDOW_EX_STYLE.WS_EX_TRANSPARENT : style & ~WINDOW_EX_STYLE.WS_EX_TRANSPARENT;
        PInvoke.SetWindowLong(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, (int)style);
    }

    public bool AreDragKeysHeld()
    {
        return IsDown(VIRTUAL_KEY.VK_CONTROL) && IsDown(VIRTUAL_KEY.VK_MENU);
    }

    public TimeSpan DoubleClickTime()
    {
        uint milliseconds = PInvoke.GetDoubleClickTime();
        return TimeSpan.FromMilliseconds(milliseconds == 0 ? 500 : milliseconds);
    }

    public bool IsCursorOver(nint windowHandle)
    {
        if (!PInvoke.GetCursorPos(out System.Drawing.Point cursor) || !PInvoke.GetWindowRect(new HWND(windowHandle), out RECT rect))
        {
            return false;
        }

        return cursor.X >= rect.left && cursor.X < rect.right && cursor.Y >= rect.top && cursor.Y < rect.bottom;
    }

    public unsafe bool IsFullscreenAppInFront()
    {
        HWND window = PInvoke.GetForegroundWindow();
        if (window.IsNull)
        {
            return false;
        }

        uint processId;
        PInvoke.GetWindowThreadProcessId(window, &processId);
        if (processId == _ownProcessId || ShellClasses.Contains(ClassNameOf(window))
            || (ProcessNameOf(processId) is string name && ShellProcesses.Contains(name)))
        {
            return false;
        }

        var style = (WINDOW_STYLE)PInvoke.GetWindowLong(window, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        if ((style & WINDOW_STYLE.WS_CAPTION) == WINDOW_STYLE.WS_CAPTION)
        {
            return false;
        }

        if (!PInvoke.GetWindowRect(window, out RECT rect))
        {
            return false;
        }

        HMONITOR monitor = PInvoke.MonitorFromWindow(window, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        if (!PInvoke.GetMonitorInfo(monitor, &info))
        {
            return false;
        }

        RECT screen = info.rcMonitor;
        return rect.left <= screen.left && rect.top <= screen.top && rect.right >= screen.right && rect.bottom >= screen.bottom;
    }

    public unsafe MaximizedApp? MaximizedAppInFront()
    {
        HWND window = PInvoke.GetForegroundWindow();
        if (window.IsNull || !PInvoke.IsZoomed(window))
        {
            return null;
        }

        uint processId;
        PInvoke.GetWindowThreadProcessId(window, &processId);
        if (processId == _ownProcessId || ShellClasses.Contains(ClassNameOf(window)))
        {
            return null;
        }

        HMONITOR monitor = PInvoke.MonitorFromWindow(window, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        if (!PInvoke.GetMonitorInfo(monitor, &info))
        {
            return null;
        }

        RECT screen = info.rcMonitor;
        string? app = ProcessNameOf(processId);
        return app is null ? null : new MaximizedApp(app, new ScreenBox(screen.left, screen.top, screen.right, screen.bottom));
    }

    public ScreenBox? TaskbarBox()
    {
        HWND taskbar = PInvoke.FindWindow("Shell_TrayWnd", null);
        if (taskbar.IsNull || !PInvoke.GetWindowRect(taskbar, out RECT rect))
        {
            return null;
        }

        return new ScreenBox(rect.left, rect.top, rect.right, rect.bottom);
    }

    // Checked every second; the same window in front is looked up once.
    private uint _lastProcessId;
    private string? _lastProcessName;

    private string? ProcessNameOf(uint processId)
    {
        if (processId != _lastProcessId)
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById((int)processId);
                _lastProcessName = process.ProcessName;
            }
            catch (ArgumentException)
            {
                _lastProcessName = null; // Exited meanwhile.
            }

            _lastProcessId = processId;
        }

        return _lastProcessName;
    }

    private static unsafe string ClassNameOf(HWND window)
    {
        const int MaxLength = 256;
        char* buffer = stackalloc char[MaxLength];
        int length = PInvoke.GetClassName(window, buffer, MaxLength);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    private static bool IsDown(VIRTUAL_KEY key)
    {
        return ((ushort)PInvoke.GetAsyncKeyState((int)key) & KeyDownBit) != 0;
    }
}
