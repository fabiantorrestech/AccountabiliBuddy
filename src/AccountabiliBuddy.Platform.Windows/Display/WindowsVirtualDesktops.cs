using AccountabiliBuddy.Core.Ports;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace AccountabiliBuddy.Platform.Windows.Display;

/// <summary>
/// The documented <c>IVirtualDesktopManager</c>. It can't name the current desktop, only tell which desktop a window is on, so the
/// current one is read from another process's window that is on it: the foreground window first, else any visible window that
/// <c>IsWindowOnCurrentVirtualDesktop</c> accepts. It can move this process's own windows, which is all the cards need.
/// </summary>
public sealed class WindowsVirtualDesktops : IVirtualDesktops
{
    private const long RetryAfterMilliseconds = 10_000;

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

    private static Guid FindCurrentDesktop(IVirtualDesktopManager manager)
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

            return found == Guid.Empty; // stop at the first one
        }, default);
        return found;
    }

    /// <returns>The desktop the window is on, or <see cref="Guid.Empty"/> if it is ours, not on the current desktop or on none (pinned).</returns>
    private static unsafe Guid DesktopOf(IVirtualDesktopManager manager, HWND window, uint ownProcess)
    {
        if (window == HWND.Null)
        {
            return Guid.Empty;
        }

        uint processId;
        PInvoke.GetWindowThreadProcessId(window, &processId);
        if (processId == ownProcess)
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
        catch (Exception)
        {
            // Windows that are on every desktop have no single one: the next window is tried.
            return Guid.Empty;
        }
    }
}
