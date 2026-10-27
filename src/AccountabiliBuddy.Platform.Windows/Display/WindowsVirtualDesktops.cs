using System.Runtime.InteropServices;
using AccountabiliBuddy.Core.Ports;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace AccountabiliBuddy.Platform.Windows.Display;

/// <summary>
/// The documented <c>IVirtualDesktopManager</c>. It can't name the current desktop, only tell which desktop a window is on, so the
/// current one is read from another process's window that is on it: the foreground window first, else any visible window that
/// <c>IsWindowOnCurrentVirtualDesktop</c> accepts. Shell windows (the desktop, the taskbar) are on every desktop and never used.
/// It can move this process's own windows, which is all the cards need.
/// </summary>
public sealed class WindowsVirtualDesktops : IVirtualDesktops
{
    private const long RetryAfterMilliseconds = 10_000;

    // TYPE_E_ELEMENTNOTFOUND: the window is on no single desktop (pinned). Any other failure means the manager is dead.
    private const int ElementNotFound = unchecked((int)0x8002802B);

    // Windows that belong to every desktop; the id they report, if any, is not the current desktop's.
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
    };

    private IVirtualDesktopManager? _manager;
    private long _retryAtTick;

    public bool IsOnCurrentDesktop(nint windowHandle)
    {
        IVirtualDesktopManager? manager = Manager();
        if (manager is null)
        {
            return true;
        }

        try
        {
            manager.IsWindowOnCurrentVirtualDesktop(new HWND(windowHandle), out BOOL onCurrent);
            return onCurrent;
        }
        catch (Exception)
        {
            // The object may be dead (Explorer restarted): a new one is made at the next call.
            _manager = null;
            return true;
        }
    }

    public bool MoveToCurrentDesktop(IReadOnlyList<nint> windowHandles)
    {
        IVirtualDesktopManager? manager = Manager();
        if (manager is null)
        {
            return false;
        }

        try
        {
            Guid desktop = FindCurrentDesktop(manager);
            if (desktop == Guid.Empty)
            {
                return false;
            }

            foreach (nint handle in windowHandles)
            {
                manager.MoveWindowToDesktop(new HWND(handle), in desktop);
            }

            return true;
        }
        catch (Exception)
        {
            _manager = null; // see IsOnCurrentDesktop
            return false;
        }
    }

    private IVirtualDesktopManager? Manager()
    {
        if (_manager is not null || Environment.TickCount64 < _retryAtTick)
        {
            return _manager;
        }

        try
        {
            _manager = (IVirtualDesktopManager)new VirtualDesktopManager();
        }
        catch (Exception)
        {
            // No virtual desktop support, or Explorer isn't up: the cards stay where they are, and it is tried again in a while.
            _retryAtTick = Environment.TickCount64 + RetryAfterMilliseconds;
        }

        return _manager;
    }

    private Guid FindCurrentDesktop(IVirtualDesktopManager manager)
    {
        uint ownProcess = (uint)Environment.ProcessId;

        Guid fromForeground = DesktopOf(manager, PInvoke.GetForegroundWindow(), ownProcess);
        if (fromForeground != Guid.Empty)
        {
            return fromForeground;
        }

        // The foreground window can be ours, the taskbar or nothing: any visible window of another process that is on the current desktop will do.
        Guid found = Guid.Empty;
        PInvoke.EnumWindows((window, _) =>
        {
            if (PInvoke.IsWindowVisible(window))
            {
                found = DesktopOf(manager, window, ownProcess);
            }

            return found == Guid.Empty && _manager is not null; // stop at the first one, or when the manager died
        }, default);
        return found;
    }

    /// <returns>The desktop the window is on, or <see cref="Guid.Empty"/> if it is ours, a shell window, not on the current desktop or on none (pinned).</returns>
    private unsafe Guid DesktopOf(IVirtualDesktopManager manager, HWND window, uint ownProcess)
    {
        if (window == HWND.Null)
        {
            return Guid.Empty;
        }

        uint processId;
        PInvoke.GetWindowThreadProcessId(window, &processId);
        if (processId == ownProcess || ShellClasses.Contains(ReadClassName(window)))
        {
            return Guid.Empty;
        }

        try
        {
            manager.IsWindowOnCurrentVirtualDesktop(window, out BOOL onCurrent);
            if (!onCurrent)
            {
                return Guid.Empty;
            }

            manager.GetWindowDesktopId(window, out Guid desktop);
            return desktop;
        }
        catch (COMException exception) when (exception.HResult == ElementNotFound)
        {
            // Windows that are on every desktop have no single one: the next window is tried.
            return Guid.Empty;
        }
        catch (Exception)
        {
            _manager = null; // The manager is dead (Explorer restarted): a new one is made at the next call.
            return Guid.Empty;
        }
    }

    private static unsafe string ReadClassName(HWND window)
    {
        const int MaxLength = 256;
        char* buffer = stackalloc char[MaxLength];
        int length = PInvoke.GetClassName(window, buffer, MaxLength);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }
}
