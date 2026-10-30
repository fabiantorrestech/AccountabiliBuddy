using System.Collections.Concurrent;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace AccountabiliBuddy.Platform.Windows.Hotkeys;

/// <summary>
/// Registers one global shortcut with RegisterHotKey. Hotkeys belong to the thread that registered them,
/// so registration and WM_HOTKEY delivery both happen on a dedicated message-loop thread; requests from
/// other threads are queued to it. <see cref="Pressed"/> is raised on that thread.
/// </summary>
public sealed class Win32GlobalHotkey : IGlobalHotkey, IDisposable
{
    private const int HotkeyId = 1;
    private const int ErrorHotkeyAlreadyRegistered = 1409;

    private readonly ConcurrentQueue<Action> _requests = new();
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private uint _threadId;
    private bool _registered;

    public Win32GlobalHotkey()
    {
        _thread = new Thread(RunMessageLoop) { IsBackground = true, Name = "AccountabiliBuddy hotkey" };
        _thread.Start();
        _ready.Wait();
    }

    public event EventHandler? Pressed;

    public bool TryRegister(HotkeyChord chord, out string? error)
    {
        (bool ok, string? message) = RunOnLoop(() =>
        {
            UnregisterOnLoop();
            if (!TryGetVirtualKey(chord.Key, out uint virtualKey))
            {
                return (false, $"'{chord.Key}' is not a supported key.");
            }

            HOT_KEY_MODIFIERS modifiers = ToNative(chord.Modifiers) | HOT_KEY_MODIFIERS.MOD_NOREPEAT;
            if (PInvoke.RegisterHotKey(HWND.Null, HotkeyId, modifiers, virtualKey))
            {
                _registered = true;
                return (true, (string?)null);
            }

            int code = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            return (false, code == ErrorHotkeyAlreadyRegistered
                ? $"{chord} is already used by another app or by Windows. Pick a different shortcut."
                : $"Windows refused {chord} (error {code}).");
        });

        error = message;
        return ok;
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
        // Make sure the thread has a message queue before anyone posts to it.
        PInvoke.PeekMessage(out _, HWND.Null, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_NOREMOVE);
        _ready.Set();

        while (PInvoke.GetMessage(out MSG message, HWND.Null, 0, 0) > 0)
        {
            if (message.message == PInvoke.WM_HOTKEY && (int)message.wParam.Value == HotkeyId)
            {
                Pressed?.Invoke(this, EventArgs.Empty);
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
        if (_registered)
        {
            PInvoke.UnregisterHotKey(HWND.Null, HotkeyId);
            _registered = false;
        }
    }

    internal static HOT_KEY_MODIFIERS ToNative(HotkeyModifiers modifiers)
    {
        HOT_KEY_MODIFIERS result = 0;
        if (modifiers.HasFlag(HotkeyModifiers.Ctrl))
        {
            result |= HOT_KEY_MODIFIERS.MOD_CONTROL;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            result |= HOT_KEY_MODIFIERS.MOD_ALT;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            result |= HOT_KEY_MODIFIERS.MOD_SHIFT;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Win))
        {
            result |= HOT_KEY_MODIFIERS.MOD_WIN;
        }

        return result;
    }

    internal static bool TryGetVirtualKey(string key, out uint virtualKey)
    {
        virtualKey = 0;
        if (key == "Space")
        {
            virtualKey = 0x20; // VK_SPACE
            return true;
        }

        uint? named = key switch
        {
            "PageUp" => 0x21, // VK_PRIOR
            "PageDown" => 0x22, // VK_NEXT
            "Left" => 0x25,
            "Up" => 0x26,
            "Right" => 0x27,
            "Down" => 0x28,
            "Plus" => 0xBB, // VK_OEM_PLUS
            "Minus" => 0xBD, // VK_OEM_MINUS
            _ => null,
        };
        if (named is uint namedKey)
        {
            virtualKey = namedKey;
            return true;
        }

        if (key.Length == 1 && (char.IsAsciiLetterUpper(key[0]) || char.IsAsciiDigit(key[0])))
        {
            virtualKey = key[0]; // VK codes for A–Z and 0–9 equal their ASCII codes.
            return true;
        }

        if (key.StartsWith('F') && int.TryParse(key[1..], out int number) && number is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x70 + number - 1); // VK_F1 = 0x70
            return true;
        }

        return false;
    }
}
