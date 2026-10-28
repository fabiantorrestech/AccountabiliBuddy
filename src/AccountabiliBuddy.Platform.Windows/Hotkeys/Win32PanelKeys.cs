using System.Collections.Concurrent;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace AccountabiliBuddy.Platform.Windows.Hotkeys;

/// <summary>
/// Several global shortcuts on one message-loop thread, registered only while the overlay panel is open: while Ctrl+Alt is held the panel's
/// letters arrive as Ctrl+Alt+D and so on, which no other app should be getting at that moment. Shortcuts another app already holds fail to
/// register and are reported back; the panel then works by mouse for those.
/// </summary>
public sealed class Win32PanelKeys : IPanelKeys, IDisposable
{
    private readonly ConcurrentQueue<Action> _requests = new();
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private readonly Dictionary<int, HotkeyChord> _registered = [];
    private uint _threadId;

    public Win32PanelKeys()
    {
        _thread = new Thread(RunMessageLoop) { IsBackground = true, Name = "AccountabiliBuddy panel keys" };
        _thread.Start();
        _ready.Wait();
    }

    public event EventHandler<HotkeyChord>? Pressed;

    public IReadOnlyList<HotkeyChord> Register(IReadOnlyList<HotkeyChord> chords)
    {
        return RunOnLoop(() =>
        {
            UnregisterOnLoop();
            var failed = new List<HotkeyChord>();
            int id = 1;
            foreach (HotkeyChord chord in chords)
            {
                if (Win32GlobalHotkey.TryGetVirtualKey(chord.Key, out uint key)
                    && PInvoke.RegisterHotKey(HWND.Null, id, Win32GlobalHotkey.ToNative(chord.Modifiers) | HOT_KEY_MODIFIERS.MOD_NOREPEAT, key))
                {
                    _registered[id++] = chord;
                }
                else
                {
                    failed.Add(chord);
                }
            }

            return (IReadOnlyList<HotkeyChord>)failed;
        });
    }

    public void Unregister()
    {
        RunOnLoop(() =>
        {
            UnregisterOnLoop();
            return true;
        });
    }

    public void Dispose()
    {
        PInvoke.PostThreadMessage(_threadId, PInvoke.WM_QUIT, default, default);
        _thread.Join();
        _ready.Dispose();
    }

    private T RunOnLoop<T>(Func<T> request)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _requests.Enqueue(() => completion.SetResult(request()));
        PInvoke.PostThreadMessage(_threadId, PInvoke.WM_APP, default, default);
        return completion.Task.GetAwaiter().GetResult();
    }

    private void RunMessageLoop()
    {
        _threadId = PInvoke.GetCurrentThreadId();
        PInvoke.PeekMessage(out _, HWND.Null, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_NOREMOVE);
        _ready.Set();
        while (PInvoke.GetMessage(out MSG message, HWND.Null, 0, 0) > 0)
        {
            if (message.message == PInvoke.WM_HOTKEY && _registered.TryGetValue((int)message.wParam.Value, out HotkeyChord? chord))
            {
                Pressed?.Invoke(this, chord);
            }
            else if (message.message == PInvoke.WM_APP)
            {
                while (_requests.TryDequeue(out Action? request))
                {
                    request();
                }
            }
        }

        UnregisterOnLoop();
    }

    private void UnregisterOnLoop()
    {
        foreach (int id in _registered.Keys)
        {
            PInvoke.UnregisterHotKey(HWND.Null, id);
        }

        _registered.Clear();
    }
}
