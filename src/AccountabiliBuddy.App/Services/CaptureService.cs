using AccountabiliBuddy.App.Views;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Capture at the source (redesign phase 2, K): Ctrl+Alt+N anywhere opens a one-line popup on the screen under the cursor;
/// what's typed becomes a come-back. Works during focus (it changes nothing). Use from the UI thread only.
/// </summary>
public sealed class CaptureService : IDisposable
{
    /// <summary>Fixed for now; Settings can make it a choice later.</summary>
    public static readonly HotkeyChord Shortcut = new(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, "N");

    private readonly ComebackStore _comebacks;
    private readonly IGlobalHotkey _hotkey;
    private readonly IMonitorLocator _monitors;
    private CapturePopup? _popup;

    public CaptureService(ComebackStore comebacks, IGlobalHotkey hotkey, IMonitorLocator monitors)
    {
        _comebacks = comebacks;
        _hotkey = hotkey;
        _monitors = monitors;
        _hotkey.Pressed += (_, _) => Dispatcher.UIThread.Post(Show);
        HotkeyError = _hotkey.TryRegister(Shortcut, out string? error) ? null : error;
    }

    /// <summary>Why the shortcut isn't active (another app has it), or null.</summary>
    public string? HotkeyError { get; }

    /// <summary>Opens the popup (or brings it back to the front).</summary>
    public void Show()
    {
        if (_popup is not null)
        {
            _popup.Activate();
            return;
        }

        var popup = new CapturePopup();
        popup.Saved += (_, text) => _ = _comebacks.AddAsync(text);
        popup.Closed += (_, _) => _popup = null;
        if (_monitors.Find(_monitors.FindMonitorUnderCursor()) is MonitorArea area)
        {
            MonitorPlacement.Center(popup, area);
        }

        _popup = popup;
        popup.Show();
        popup.Activate();
    }

    public void Dispose()
    {
        _hotkey.Unregister();
        (_hotkey as IDisposable)?.Dispose();
    }
}
