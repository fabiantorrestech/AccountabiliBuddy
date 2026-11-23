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
    private bool _registered;

    public CaptureService(ComebackStore comebacks, IGlobalHotkey hotkey, IMonitorLocator monitors)
    {
        _comebacks = comebacks;
        _hotkey = hotkey;
        _monitors = monitors;
        _hotkey.Pressed += (_, _) => Dispatcher.UIThread.Post(Show);
        SetHotkeyOn(true);
    }

    /// <summary>True after the shutdown routine was finished (until the next work part): the popup says a thought waits for tomorrow. Set by the app.</summary>
    public Func<bool>? IsWorkOver { get; set; }

    /// <summary>Why the shortcut isn't active (another app has it), or null.</summary>
    public string? HotkeyError { get; private set; }

    /// <summary>
    /// Holds or lets go of Ctrl+Alt+N system-wide: the app lets go of it while the overlay is hidden and keeps no shortcuts then, so other
    /// apps get it (owner, AB-Bugs #31; <see cref="SessionOverlayService.ReleasesShortcuts"/>).
    /// </summary>
    public void SetHotkeyOn(bool on)
    {
        if (on == _registered)
        {
            return;
        }

        if (on)
        {
            _registered = _hotkey.TryRegister(Shortcut, out string? error);
            HotkeyError = _registered ? null : error;
        }
        else
        {
            _hotkey.Unregister();
            _registered = false;
            HotkeyError = null;
        }
    }

    /// <summary>Opens the popup (or brings it back to the front).</summary>
    public void Show()
    {
        if (_popup is not null)
        {
            _popup.Activate();
            return;
        }

        var popup = new CapturePopup();
        if (IsWorkOver?.Invoke() == true)
        {
            popup.HintText = "Work is over: Enter saves it under Come back to, for tomorrow · Esc drops it";
        }

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
