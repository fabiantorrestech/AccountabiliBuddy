using System.Collections.Concurrent;
using System.Drawing;
using AccountabiliBuddy.Core.Ports;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace AccountabiliBuddy.Platform.Windows.Hotkeys;

/// <summary>
/// The pointer going into or out of one screen area, from a low-level mouse hook on its own message-loop thread (like
/// <see cref="Win32CtrlAltWatcher"/>). The hook only compares the pointer's position with the area and passes every mouse event on
/// untouched (nothing is stored or logged). It is installed only while an area is watched; Windows calls it only when the mouse moves.
/// Positions are physical screen pixels (the app is per-monitor DPI aware), the same as the overlay window's own box.
/// </summary>
public sealed unsafe class Win32CursorWatcher : ICursorWatcher, IDisposable
{
    private static Win32CursorWatcher? _instance;
    private static readonly HOOKPROC HookProcedure = OnMouse;

    private readonly ConcurrentQueue<Action> _requests = new();
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private uint _threadId;
    private HHOOK _hook;
    private volatile PixelBox? _area;
    private volatile bool _inside;

    public Win32CursorWatcher()
    {
        _instance = this;
        _thread = new Thread(RunMessageLoop) { IsBackground = true, Name = "AccountabiliBuddy cursor watcher" };
        _thread.Start();
        _ready.Wait();
    }

    public bool IsInside => _inside;

    public event EventHandler<bool>? InsideChanged;

    /// <remarks>Watching again after a stop installs a fresh hook: Windows removes a hook that answered too slowly once, without telling us.</remarks>
    public void Watch(PixelBox? area)
    {
        _requests.Enqueue(() =>
        {
            if (area == _area)
            {
                return;
            }

            _area = area;
            if (area is null)
            {
                if (!_hook.IsNull)
                {
                    PInvoke.UnhookWindowsHookEx(_hook);
                    _hook = default;
                }

                SetInside(false);
                return;
            }

            // The pointer may already be inside when the area appears or moves.
            SetInside(PInvoke.GetCursorPos(out Point at) && Contains(area, at.X, at.Y));
            if (_hook.IsNull)
            {
                _hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL, HookProcedure, HINSTANCE.Null, 0);
            }
        });
        PInvoke.PostThreadMessage(_threadId, PInvoke.WM_APP, default, default);
    }

    public void Dispose()
    {
        PInvoke.PostThreadMessage(_threadId, PInvoke.WM_QUIT, default, default);
        _thread.Join();
        _ready.Dispose();
    }

    private void RunMessageLoop()
    {
        _threadId = PInvoke.GetCurrentThreadId();
        PInvoke.PeekMessage(out _, HWND.Null, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_NOREMOVE);
        _ready.Set();
        while (PInvoke.GetMessage(out MSG message, HWND.Null, 0, 0) > 0)
        {
            if (message.message == PInvoke.WM_APP)
            {
                while (_requests.TryDequeue(out Action? request))
                {
                    request();
                }
            }
        }

        if (!_hook.IsNull)
        {
            PInvoke.UnhookWindowsHookEx(_hook);
        }
    }

    private static LRESULT OnMouse(int code, WPARAM wParam, LPARAM lParam)
    {
        // No exception may cross this native callback; the event is always passed on. Only a comparison happens here.
        try
        {
            if (code >= 0 && wParam.Value == PInvoke.WM_MOUSEMOVE && _instance is { _area: { } area } self)
            {
                var info = (MSLLHOOKSTRUCT*)lParam.Value;
                self.SetInside(Contains(area, info->pt.X, info->pt.Y));
            }
        }
        catch
        {
        }

        return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);
    }

    private static bool Contains(PixelBox area, int x, int y) => x >= area.Left && x < area.Right && y >= area.Top && y < area.Bottom;

    private void SetInside(bool inside)
    {
        if (inside != _inside)
        {
            _inside = inside;
            InsideChanged?.Invoke(this, inside);
        }
    }
}
