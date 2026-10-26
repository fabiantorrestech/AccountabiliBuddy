using AccountabiliBuddy.Core.Ports;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace AccountabiliBuddy.Platform.Windows.Display;

/// <summary>
/// FlashWindowEx with FLASHW_TRAY | FLASHW_TIMERNOFG: only the taskbar button flashes (orange), and it keeps flashing
/// until the window comes to the front. Nothing is activated and the keyboard stays where it is.
/// </summary>
public sealed class WindowsWindowAttention : IWindowAttention
{
    public unsafe void Flash(nint windowHandle)
    {
        var info = new FLASHWINFO
        {
            cbSize = (uint)sizeof(FLASHWINFO),
            hwnd = new HWND(windowHandle),
            dwFlags = FLASHWINFO_FLAGS.FLASHW_TRAY | FLASHWINFO_FLAGS.FLASHW_TIMERNOFG,
            uCount = 0,
            dwTimeout = 0,
        };
        PInvoke.FlashWindowEx(in info);
    }
}
