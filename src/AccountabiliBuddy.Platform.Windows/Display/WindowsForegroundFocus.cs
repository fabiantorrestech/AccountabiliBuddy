using AccountabiliBuddy.Core.Ports;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace AccountabiliBuddy.Platform.Windows.Display;

/// <summary>
/// Windows only lets the foreground program give the foreground away, so a plain SetForegroundWindow from here just flashes the
/// taskbar button. Attaching this thread's input to the foreground window's thread for the call makes it count as the same
/// program's request (the usual, documented-by-use workaround).
/// </summary>
public sealed class WindowsForegroundFocus : IForegroundFocus
{
    public unsafe bool IsForegroundForeign()
    {
        HWND foreground = PInvoke.GetForegroundWindow();
        if (foreground == HWND.Null)
        {
            return true;
        }

        uint processId;
        PInvoke.GetWindowThreadProcessId(foreground, &processId);
        return processId != (uint)Environment.ProcessId;
    }

    public unsafe void TakeForeground(nint windowHandle)
    {
        var window = new HWND(windowHandle);
        HWND foreground = PInvoke.GetForegroundWindow();
        uint foregroundThread = foreground == HWND.Null ? 0 : PInvoke.GetWindowThreadProcessId(foreground, null);
        uint thisThread = PInvoke.GetCurrentThreadId();
        bool attached = foregroundThread != 0 && foregroundThread != thisThread
            && PInvoke.AttachThreadInput(thisThread, foregroundThread, true);
        try
        {
            PInvoke.BringWindowToTop(window);
            PInvoke.SetForegroundWindow(window);
        }
        finally
        {
            if (attached)
            {
                PInvoke.AttachThreadInput(thisThread, foregroundThread, false);
            }
        }
    }
}
