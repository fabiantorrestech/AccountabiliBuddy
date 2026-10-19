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
/// A fullscreen app is a foreground window whose rectangle covers its monitor (maximized windows leave the taskbar).
/// </summary>
public sealed class Win32OverlayWindowControl : IOverlayWindowControl
{
    private const ushort KeyDownBit = 0x8000;

    // The desktop and the taskbar cover the screen too but aren't fullscreen apps.
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal) { "Progman", "WorkerW", "Shell_TrayWnd" };

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

    public unsafe bool IsFullscreenAppInFront()
    {
        HWND window = PInvoke.GetForegroundWindow();
        if (window.IsNull)
        {
            return false;
        }

        uint processId;
        PInvoke.GetWindowThreadProcessId(window, &processId);
        if (processId == _ownProcessId || ShellClasses.Contains(ClassNameOf(window)))
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
