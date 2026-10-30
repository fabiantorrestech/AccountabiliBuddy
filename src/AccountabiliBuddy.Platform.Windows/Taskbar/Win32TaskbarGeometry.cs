using System.Diagnostics;
using AccountabiliBuddy.Core.Ports;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace AccountabiliBuddy.Platform.Windows.Taskbar;

/// <summary>
/// Reads where the taskbars are (ADR 51) and tells when that changes, without polling. One thread owns a hidden top-level window
/// that is registered as an appbar (it reserves nothing; it only receives <c>ABN_*</c> notifications) and also gets Windows'
/// display, DPI and setting change messages, plus two window event hooks: a new window in front, and the taskbar's own thread moving
/// its window (an auto-hiding taskbar sliding in or out). Each message only sets a flag and posts one "read again" message, so a burst
/// (a slide, a monitor change) is read once. Where the app icons end and the tray starts comes from <see cref="TaskbarContentProbe"/>
/// on the thread pool, since UI Automation talks to Explorer and may be slow; its answer is kept until the taskbar's own box changes or a
/// window comes to the front (a new app may have added an icon).
/// </summary>
public sealed unsafe class Win32TaskbarGeometry : ITaskbarGeometry, IDisposable
{
    private const string ClassName = "AccountabiliBuddyTaskbarWatcher";
    private const uint AppBarCallback = PInvoke.WM_APP + 2;
    private const uint ReadAgain = PInvoke.WM_APP + 3;
    private const uint ProbeDone = PInvoke.WM_APP + 4;

    // The taskbar is "shown" when this many pixels of it are on screen at least (an auto-hiding one leaves a strip of 1-2).
    private const int MinShownThickness = 8;

    // MONITORINFO.dwFlags bit for the main monitor.
    private const uint MonitorInfoPrimary = 1;

    // The window in front is looked at again at most this often for new icons (a probe takes UI Automation round trips).
    private static readonly TimeSpan ProbeGap = TimeSpan.FromMilliseconds(400);

    // A new window in front only re-reads the taskbar this often: an app may have added an icon, but a read costs a walk over Explorer's tree.
    private static readonly TimeSpan ForegroundProbeGap = TimeSpan.FromSeconds(2);

    // The desktop and the taskbar cover the screen too but are not full screen apps.
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal) { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

    private static Win32TaskbarGeometry? _instance;
    private static readonly WNDPROC WindowProcedure = OnWindowMessage;
    private static readonly WINEVENTPROC EventProcedure = OnWinEvent;

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private readonly object _gate = new();
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;
    private readonly TaskbarContentProbe _probe = new();

    private HWND _window;
    private HWINEVENTHOOK _foregroundHook;
    private HWINEVENTHOOK _locationHook;
    private uint _locationThread;
    private uint _taskbarCreatedMessage;
    private int _dirty;
    private int _probing;
    private long _lastProbeTick;
    private volatile bool _needsProbe;
    // The last reading of the main taskbar, key and result published together (written on the thread pool, read on the loop thread).
    private volatile ProbeReading? _reading;
    private uint _lastProcessId;
    private string? _lastProcessName;
    private IReadOnlyList<TaskbarInfo> _taskbars = [];
    private ForegroundScreenState _foreground = ForegroundScreenState.None;
    private bool _disposed;

    public Win32TaskbarGeometry()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "AccountabiliBuddy taskbar watcher" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait();
    }

    public IReadOnlyList<TaskbarInfo> Taskbars
    {
        get
        {
            lock (_gate)
            {
                return _taskbars;
            }
        }
    }

    public ForegroundScreenState Foreground
    {
        get
        {
            lock (_gate)
            {
                return _foreground;
            }
        }
    }

    public event EventHandler? Changed;

    public void Refresh()
    {
        RequestRead();
    }

    public void InvokeTrayOverflow()
    {
        Task.Run(() => TryInvoke(["NotificationChevron"], [], ["hidden icons", "Notification Chevron"]));
    }

    public void InvokeClock()
    {
        Task.Run(() => TryInvoke(["ClockButton"], ["TrayClockWClass"], []));
    }

    private static void TryInvoke(string[] ids, string[] classes, string[] names)
    {
        try
        {
            TaskbarInvoker.Invoke(ids, classes, names);
        }
        catch (Exception)
        {
            // Explorer is busy or restarting; the click is lost, nothing else.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!_window.IsNull)
        {
            PInvoke.PostMessage(_window, PInvoke.WM_DESTROY, default, default);
        }

        _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }

    private void RequestRead()
    {
        if (!_window.IsNull && Interlocked.Exchange(ref _dirty, 1) == 0)
        {
            PInvoke.PostMessage(_window, ReadAgain, default, default);
        }
    }

    private void Run()
    {
        _instance = this;
        fixed (char* className = ClassName)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = WindowProcedure,
                hInstance = (HINSTANCE)PInvoke.GetModuleHandle((PCWSTR)null).Value,
                lpszClassName = className,
            };
            PInvoke.RegisterClassEx(in windowClass);

            // A top-level window that is never shown: message-only windows do not get Windows' broadcasts (display, DPI, settings).
            _window = PInvoke.CreateWindowEx(
                WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE, ClassName, ClassName, WINDOW_STYLE.WS_POPUP,
                0, 0, 0, 0, HWND.Null, null, null, null);
        }

        if (!_window.IsNull)
        {
            _taskbarCreatedMessage = PInvoke.RegisterWindowMessage("TaskbarCreated");
            RegisterAppBar();
            _foregroundHook = PInvoke.SetWinEventHook(
                PInvoke.EVENT_SYSTEM_FOREGROUND, PInvoke.EVENT_SYSTEM_FOREGROUND, HMODULE.Null, EventProcedure, 0, 0, PInvoke.WINEVENT_OUTOFCONTEXT);
            HookTaskbarLocation();
            ReadNow();
        }

        _ready.Set();
        if (_window.IsNull)
        {
            return;
        }

        while (PInvoke.GetMessage(out MSG message, HWND.Null, 0, 0) > 0)
        {
            PInvoke.TranslateMessage(in message);
            PInvoke.DispatchMessage(in message);
        }

        if (!_foregroundHook.IsNull)
        {
            PInvoke.UnhookWinEvent(_foregroundHook);
        }

        if (!_locationHook.IsNull)
        {
            PInvoke.UnhookWinEvent(_locationHook);
        }

        UnregisterAppBar();
        PInvoke.UnregisterClass(ClassName, (System.Runtime.InteropServices.SafeHandle?)null);
    }

    private static LRESULT OnWindowMessage(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        Win32TaskbarGeometry? self = _instance;
        if (self is null)
        {
            return PInvoke.DefWindowProc(window, message, wParam, lParam);
        }

        if (message == AppBarCallback)
        {
            // ABN_FULLSCREENAPP, ABN_STATECHANGE (auto-hide toggled), ABN_POSCHANGED (edge or size), ABN_WINDOWARRANGE.
            self.InvalidateProbe();
            self.RequestRead();
            return default;
        }

        if (message == ReadAgain)
        {
            Interlocked.Exchange(ref self._dirty, 0);
            self.ReadNow();
            return default;
        }

        if (message == ProbeDone)
        {
            self.ReadNow();
            return default;
        }

        if (message == PInvoke.WM_DISPLAYCHANGE || message == PInvoke.WM_DPICHANGED || message == PInvoke.WM_SETTINGCHANGE)
        {
            // Resolution, monitors or scaling changed, or a Windows setting (taskbar alignment, auto-hide, size) did.
            self.InvalidateProbe();
            self.RequestRead();
            return default;
        }

        if (message == self._taskbarCreatedMessage && message != 0)
        {
            // Explorer restarted: its taskbar is a new window, and our appbar registration died with it.
            self.UnregisterAppBar();
            self.RegisterAppBar();
            self.HookTaskbarLocation();
            self.InvalidateProbe();
            self.RequestRead();
            return default;
        }

        if (message == PInvoke.WM_DESTROY)
        {
            PInvoke.PostQuitMessage(0);
            return default;
        }

        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }

    private static void OnWinEvent(HWINEVENTHOOK hook, uint eventType, HWND window, int idObject, int idChild, uint eventThread, uint eventTime)
    {
        Win32TaskbarGeometry? self = _instance;
        if (self is null)
        {
            return;
        }

        if (eventType == PInvoke.EVENT_SYSTEM_FOREGROUND)
        {
            // A new window in front: it may be full screen, and the app may have added an icon to the taskbar.
            self._needsProbe = true;
            self.RequestRead();
            return;
        }

        if (eventType == PInvoke.EVENT_OBJECT_LOCATIONCHANGE && idObject == 0 /* OBJID_WINDOW */ && IsTaskbarWindow(window))
        {
            // The taskbar slid in or out (auto-hide) or moved.
            self.RequestRead();
        }
    }

    private static bool IsTaskbarWindow(HWND window)
    {
        string className = ClassNameOf(window);
        return className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    private void InvalidateProbe()
    {
        _reading = null;
    }

    private void RegisterAppBar()
    {
        var data = new AppBarInterop.AppBarData
        {
            CbSize = (uint)sizeof(AppBarInterop.AppBarData),
            Window = (nint)_window.Value,
            CallbackMessage = AppBarCallback,
        };
        AppBarInterop.SHAppBarMessage(AppBarInterop.New, ref data);
    }

    private void UnregisterAppBar()
    {
        var data = new AppBarInterop.AppBarData { CbSize = (uint)sizeof(AppBarInterop.AppBarData), Window = (nint)_window.Value };
        AppBarInterop.SHAppBarMessage(AppBarInterop.Remove, ref data);
    }

    /// <summary>Listens to the main taskbar's own thread only, so a slide shows up without hearing every window move on the desktop.</summary>
    private void HookTaskbarLocation()
    {
        if (!_locationHook.IsNull)
        {
            PInvoke.UnhookWinEvent(_locationHook);
            _locationHook = default;
        }

        HWND tray = PInvoke.FindWindow("Shell_TrayWnd", null);
        if (tray.IsNull)
        {
            return;
        }

        uint processId;
        _locationThread = PInvoke.GetWindowThreadProcessId(tray, &processId);
        _locationHook = PInvoke.SetWinEventHook(
            PInvoke.EVENT_OBJECT_LOCATIONCHANGE, PInvoke.EVENT_OBJECT_LOCATIONCHANGE, HMODULE.Null, EventProcedure, processId, _locationThread,
            PInvoke.WINEVENT_OUTOFCONTEXT);
    }

    private void ReadNow()
    {
        try
        {
            ReadAndPublish();
        }
        catch (Exception)
        {
            // Never let an exception cross a window procedure or a WinEvent callback: the next event reads again.
        }
    }

    private void ReadAndPublish()
    {
        IReadOnlyList<TaskbarInfo> taskbars = ReadTaskbars();
        ForegroundScreenState foreground = ReadForeground();
        bool changed;
        lock (_gate)
        {
            changed = !SameTaskbars(_taskbars, taskbars) || _foreground != foreground;
            _taskbars = taskbars;
            _foreground = foreground;
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static bool SameTaskbars(IReadOnlyList<TaskbarInfo> a, IReadOnlyList<TaskbarInfo> b)
    {
        return a.Count == b.Count && a.Zip(b).All(pair => pair.First == pair.Second);
    }

    private IReadOnlyList<TaskbarInfo> ReadTaskbars()
    {
        var found = new List<TaskbarInfo>();
        HWND primary = PInvoke.FindWindow("Shell_TrayWnd", null);
        bool autoHide = IsAutoHide();
        var monitors = ReadMonitors();

        if (!primary.IsNull)
        {
            TaskbarInfo? info = Describe(primary, true, autoHide, monitors);
            if (info is not null)
            {
                found.Add(info);
            }
        }

        HWND secondary = HWND.Null;
        while (true)
        {
            secondary = PInvoke.FindWindowEx(HWND.Null, secondary, "Shell_SecondaryTrayWnd", null);
            if (secondary.IsNull)
            {
                break;
            }

            TaskbarInfo? info = Describe(secondary, false, autoHide, monitors);
            if (info is not null)
            {
                found.Add(info);
            }
        }

        return found;
    }

    private TaskbarInfo? Describe(HWND taskbar, bool isPrimary, bool autoHide, List<MonitorFacts> monitors)
    {
        if (!PInvoke.GetWindowRect(taskbar, out RECT rect))
        {
            return null;
        }

        var bounds = new PixelBox(rect.left, rect.top, rect.right, rect.bottom);
        MonitorFacts? monitor = monitors.FirstOrDefault(facts => Overlap(facts.Bounds, bounds) > 0) ?? monitors.FirstOrDefault(facts => facts.IsPrimary);
        if (monitor is null)
        {
            return null;
        }

        TaskbarEdge edge = isPrimary ? PrimaryEdge(taskbar, bounds, monitor.Bounds) : EdgeOf(bounds, monitor.Bounds);
        int thickness = edge is TaskbarEdge.Bottom or TaskbarEdge.Top ? bounds.Height : bounds.Width;
        int visible = VisibleThickness(bounds, monitor.Bounds, edge);
        bool shown = !autoHide || visible >= Math.Max(MinShownThickness, thickness - 4);

        PixelBox? tray = null;
        PixelBox? free = null;
        PixelBox? leftFree = null;
        bool leftAligned = true;
        if (shown && isPrimary && edge is TaskbarEdge.Bottom or TaskbarEdge.Top)
        {
            (tray, free, leftFree, leftAligned) = FindFreeArea(taskbar, bounds, edge);
        }

        return new TaskbarInfo(monitor.Id, isPrimary, monitor.Bounds, monitor.WorkArea, monitor.Scale, edge, bounds, autoHide, shown, free, tray, leftFree, leftAligned);
    }

    /// <summary>The tray's box and the free stretch between the icons and the tray, or nulls where they could not be read.</summary>
    private (PixelBox? Tray, PixelBox? Free, PixelBox? LeftFree, bool LeftAligned) FindFreeArea(HWND taskbar, PixelBox bounds, TaskbarEdge edge)
    {
        var key = new ProbeKey((nint)taskbar.Value, bounds);
        ProbeReading? reading = _reading;
        if (reading?.Key != key || _needsProbe)
        {
            StartProbe(taskbar, bounds, key, urgent: reading?.Key != key);
        }

        (int? appsStart, int? appsEnd, int? trayStart) = reading?.Key == key ? reading.Result : (null, null, null);
        PixelBox? tray = trayStart is int start ? new PixelBox(start, bounds.Top, bounds.Right, bounds.Bottom) : TrayNotifyBox(taskbar, bounds);
        trayStart ??= tray?.Left;
        PixelBox? free = appsEnd is int end && trayStart is int tail && tail - end > 0
            ? new PixelBox(end, bounds.Top, tail, bounds.Bottom)
            : null;
        PixelBox? leftFree = appsStart is int first && first - bounds.Left > bounds.Width * 0.08
            ? new PixelBox(bounds.Left, bounds.Top, first, bounds.Bottom)
            : null;
        return (tray, free, leftFree, leftFree is null);
    }

    private void StartProbe(HWND taskbar, PixelBox bounds, ProbeKey key, bool urgent)
    {
        if (Environment.TickCount64 - _lastProbeTick < (urgent ? ProbeGap : ForegroundProbeGap).TotalMilliseconds || Interlocked.Exchange(ref _probing, 1) == 1)
        {
            return;
        }

        _lastProbeTick = Environment.TickCount64;
        _needsProbe = false;
        nint handle = (nint)taskbar.Value;
        Task.Run(() =>
        {
            (int? AppsStart, int? AppsEnd, int? TrayStart) result = (null, null, null);
            try
            {
                result = _probe.Probe(new HWND(handle), bounds);
            }
            catch (Exception)
            {
                // UI Automation can fail while Explorer restarts; the next probe tries again.
            }
            finally
            {
                _reading = new ProbeReading(key, result);
                Interlocked.Exchange(ref _probing, 0);
                if (!_window.IsNull)
                {
                    PInvoke.PostMessage(_window, ProbeDone, default, default);
                }
            }
        });
    }

    private static PixelBox? TrayNotifyBox(HWND taskbar, PixelBox bounds)
    {
        HWND tray = PInvoke.FindWindowEx(taskbar, HWND.Null, "TrayNotifyWnd", null);
        if (tray.IsNull || !PInvoke.GetWindowRect(tray, out RECT rect))
        {
            return null;
        }

        var box = new PixelBox(rect.left, rect.top, rect.right, rect.bottom);
        return box.Width > 8 && box.Left >= bounds.Left && box.Right <= bounds.Right + 2 ? box : null;
    }

    private static TaskbarEdge PrimaryEdge(HWND taskbar, PixelBox bounds, PixelBox monitor)
    {
        var data = new AppBarInterop.AppBarData { CbSize = (uint)sizeof(AppBarInterop.AppBarData), Window = (nint)taskbar.Value };
        if (AppBarInterop.SHAppBarMessage(AppBarInterop.GetTaskbarPos, ref data) != 0)
        {
            return data.Edge switch
            {
                AppBarInterop.EdgeLeft => TaskbarEdge.Left,
                AppBarInterop.EdgeTop => TaskbarEdge.Top,
                AppBarInterop.EdgeRight => TaskbarEdge.Right,
                _ => TaskbarEdge.Bottom,
            };
        }

        return EdgeOf(bounds, monitor);
    }

    private static TaskbarEdge EdgeOf(PixelBox bounds, PixelBox monitor)
    {
        if (bounds.Width >= monitor.Width * 0.9)
        {
            return bounds.Top + (bounds.Height / 2) > monitor.Top + (monitor.Height / 2) ? TaskbarEdge.Bottom : TaskbarEdge.Top;
        }

        return bounds.Left + (bounds.Width / 2) > monitor.Left + (monitor.Width / 2) ? TaskbarEdge.Right : TaskbarEdge.Left;
    }

    private static int VisibleThickness(PixelBox bounds, PixelBox monitor, TaskbarEdge edge)
    {
        int left = Math.Max(bounds.Left, monitor.Left);
        int top = Math.Max(bounds.Top, monitor.Top);
        int right = Math.Min(bounds.Right, monitor.Right);
        int bottom = Math.Min(bounds.Bottom, monitor.Bottom);
        if (right <= left || bottom <= top)
        {
            return 0;
        }

        return edge is TaskbarEdge.Bottom or TaskbarEdge.Top ? bottom - top : right - left;
    }

    private static long Overlap(PixelBox a, PixelBox b)
    {
        long width = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
        long height = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        return width > 0 && height > 0 ? width * height : 0;
    }

    private static bool IsAutoHide()
    {
        var data = new AppBarInterop.AppBarData { CbSize = (uint)sizeof(AppBarInterop.AppBarData) };
        return ((uint)AppBarInterop.SHAppBarMessage(AppBarInterop.GetState, ref data) & AppBarInterop.AutoHideState) != 0;
    }

    private static List<MonitorFacts> ReadMonitors()
    {
        var monitors = new List<MonitorFacts>();
        PInvoke.EnumDisplayMonitors(default, (RECT?)null, (monitor, _, _, _) =>
        {
            var info = new MONITORINFOEXW();
            info.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);
            if (PInvoke.GetMonitorInfo(monitor, (MONITORINFO*)&info))
            {
                double scale = 1;
                if (PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out uint dpiX, out _).Succeeded && dpiX > 0)
                {
                    scale = dpiX / 96.0;
                }

                RECT bounds = info.monitorInfo.rcMonitor;
                RECT work = info.monitorInfo.rcWork;
                monitors.Add(new MonitorFacts(
                    info.szDevice.ToString(),
                    (info.monitorInfo.dwFlags & MonitorInfoPrimary) != 0,
                    new PixelBox(bounds.left, bounds.top, bounds.right, bounds.bottom),
                    new PixelBox(work.left, work.top, work.right, work.bottom),
                    scale));
            }

            return true;
        }, default);
        return monitors;
    }

    private ForegroundScreenState ReadForeground()
    {
        HWND window = PInvoke.GetForegroundWindow();
        if (window.IsNull)
        {
            return ForegroundScreenState.None;
        }

        uint processId;
        PInvoke.GetWindowThreadProcessId(window, &processId);
        if (processId == _ownProcessId || ShellClasses.Contains(ClassNameOf(window)))
        {
            return ForegroundScreenState.None;
        }

        string? processName = ProcessNameOf(processId);

        // Windows' own screens that cover everything for a moment while switching apps (Alt+Tab, Task view, Start, Search).
        if (processName is not null && Display.Win32OverlayWindowControl.ShellProcesses.Contains(processName))
        {
            return ForegroundScreenState.None;
        }

        string className = ClassNameOf(window);
        HMONITOR monitor = PInvoke.MonitorFromWindow(window, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFOEXW();
        info.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);
        if (!PInvoke.GetMonitorInfo(monitor, (MONITORINFO*)&info))
        {
            return ForegroundScreenState.None;
        }

        var style = (WINDOW_STYLE)PInvoke.GetWindowLong(window, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        string monitorId = info.szDevice.ToString();
        if ((style & WINDOW_STYLE.WS_CAPTION) == WINDOW_STYLE.WS_CAPTION || !PInvoke.GetWindowRect(window, out RECT rect))
        {
            return new ForegroundScreenState(false, false, monitorId, processName, className);
        }

        RECT screen = info.monitorInfo.rcMonitor;
        bool covers = rect.left <= screen.left && rect.top <= screen.top && rect.right >= screen.right && rect.bottom >= screen.bottom;
        if (!covers)
        {
            return new ForegroundScreenState(false, false, monitorId, processName, className);
        }

        // Windows' own "running a full-screen game" state: Direct3D exclusive mode, where the taskbar is not over it.
        bool game = PInvoke.SHQueryUserNotificationState(out QUERY_USER_NOTIFICATION_STATE state).Succeeded
                    && state == QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN;
        return new ForegroundScreenState(true, game, monitorId, processName, className);
    }

    private string? ProcessNameOf(uint processId)
    {
        if (processId != _lastProcessId)
        {
            try
            {
                using Process process = Process.GetProcessById((int)processId);
                _lastProcessName = process.ProcessName;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                _lastProcessName = null; // Exited meanwhile, or not ours to read.
            }

            _lastProcessId = processId;
        }

        return _lastProcessName;
    }

    internal static string ClassNameOf(HWND window)
    {
        const int MaxLength = 256;
        char* buffer = stackalloc char[MaxLength];
        int length = PInvoke.GetClassName(window, buffer, MaxLength);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    private sealed record MonitorFacts(string Id, bool IsPrimary, PixelBox Bounds, PixelBox WorkArea, double Scale);

    private sealed record ProbeKey(nint Window, PixelBox Bounds);

    private sealed record ProbeReading(ProbeKey Key, (int? AppsStart, int? AppsEnd, int? TrayStart) Result);
}
