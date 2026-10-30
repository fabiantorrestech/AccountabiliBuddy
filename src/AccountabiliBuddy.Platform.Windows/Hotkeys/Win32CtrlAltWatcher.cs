using System.Collections.Concurrent;
using AccountabiliBuddy.Core.Ports;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace AccountabiliBuddy.Platform.Windows.Hotkeys;

/// <summary>
/// Ctrl+Alt held, from a low-level keyboard hook on its own message-loop thread: the hook only looks at whether Ctrl and Alt go down or up
/// and passes every key on untouched (nothing is stored or logged). Installed only while <see cref="SetListening"/> is on.
/// </summary>
public sealed unsafe class Win32CtrlAltWatcher : ICtrlAltWatcher, IDisposable
{
    private static Win32CtrlAltWatcher? _instance;
    private static readonly HOOKPROC HookProcedure = OnKey;

    private readonly ConcurrentQueue<Action> _requests = new();
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private uint _threadId;
    private HHOOK _hook;
    private bool _ctrl;
    private bool _alt;
    private volatile bool _held;

    public Win32CtrlAltWatcher()
    {
        _instance = this;
        _thread = new Thread(RunMessageLoop) { IsBackground = true, Name = "AccountabiliBuddy Ctrl+Alt watcher" };
        _thread.Start();
        _ready.Wait();
    }

    public bool IsHeld => _held;

    public event EventHandler<bool>? HeldChanged;

    public void Resync()
    {
        _requests.Enqueue(() =>
        {
            if (!_hook.IsNull)
            {
                Set((PInvoke.GetAsyncKeyState((int)VIRTUAL_KEY.VK_CONTROL) & 0x8000) != 0, (PInvoke.GetAsyncKeyState((int)VIRTUAL_KEY.VK_MENU) & 0x8000) != 0);
            }
        });
        PInvoke.PostThreadMessage(_threadId, PInvoke.WM_APP, default, default);
    }

    /// <remarks>Listening again installs a fresh hook: Windows removes a hook that answered too slowly once, without telling us.</remarks>
    public void SetListening(bool listening)
    {
        _requests.Enqueue(() =>
        {
            if (listening)
            {
                if (!_hook.IsNull)
                {
                    PInvoke.UnhookWindowsHookEx(_hook);
                    _hook = default;
                }

                Set((PInvoke.GetAsyncKeyState((int)VIRTUAL_KEY.VK_CONTROL) & 0x8000) != 0,
                    (PInvoke.GetAsyncKeyState((int)VIRTUAL_KEY.VK_MENU) & 0x8000) != 0);
                _hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, HookProcedure, HINSTANCE.Null, 0);
            }
            else if (!listening && !_hook.IsNull)
            {
                PInvoke.UnhookWindowsHookEx(_hook);
                _hook = default;
                Set(false, false);
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

    private static LRESULT OnKey(int code, WPARAM wParam, LPARAM lParam)
    {
        // No exception may cross this native callback; the key is always passed on.
        try
        {
            ForwardCtrlAlt(code, wParam, lParam);
        }
        catch
        {
        }

        return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);
    }

    private static void ForwardCtrlAlt(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code >= 0 && _instance is { } self)
        {
            var info = (KBDLLHOOKSTRUCT*)lParam.Value;
            bool down = wParam.Value is PInvoke.WM_KEYDOWN or PInvoke.WM_SYSKEYDOWN;
            bool up = wParam.Value is PInvoke.WM_KEYUP or PInvoke.WM_SYSKEYUP;
            if (down || up)
            {
                var key = (VIRTUAL_KEY)info->vkCode;
                if (key is VIRTUAL_KEY.VK_CONTROL or VIRTUAL_KEY.VK_LCONTROL or VIRTUAL_KEY.VK_RCONTROL)
                {
                    self.Set(down, self._alt);
                }
                else if (key is VIRTUAL_KEY.VK_MENU or VIRTUAL_KEY.VK_LMENU or VIRTUAL_KEY.VK_RMENU)
                {
                    self.Set(self._ctrl, down);
                }
            }
        }
    }

    private void Set(bool ctrl, bool alt)
    {
        _ctrl = ctrl;
        _alt = alt;
        bool held = ctrl && alt;
        if (held != _held)
        {
            _held = held;
            HeldChanged?.Invoke(this, held);
        }
    }
}
