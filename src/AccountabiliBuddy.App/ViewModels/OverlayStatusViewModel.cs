using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// What Today says about the on-screen overlay when it is not showing on purpose: a quiet line while Ctrl+Alt+H keeps it hidden, and a card with
/// "Turn on" when it was turned off with the Ctrl+Alt panel's Disable button (the only way back from there is in the app). Turned off in Settings
/// shows no card: the switch is right there. Both only add to what shows, so they work during a focus session too. Use from the UI thread only.
/// </summary>
public sealed partial class OverlayStatusViewModel : ObservableObject
{
    private readonly OverlaySettingsService _settings;
    private readonly SessionOverlayService _overlay;

    public OverlayStatusViewModel(OverlaySettingsService settings, SessionOverlayService overlay)
    {
        _settings = settings;
        _overlay = overlay;
        HiddenText = string.Empty;
        settings.Changed += (_, _) => Refresh();
        overlay.Changed += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>Ctrl+Alt+H keeps the overlay hidden.</summary>
    [ObservableProperty]
    public partial bool IsHidden { get; set; }

    /// <summary>"Overlay hidden · Ctrl+Alt+H to show" (without the shortcut when another program holds it).</summary>
    [ObservableProperty]
    public partial string HiddenText { get; set; }

    /// <summary>The overlay is off because of the panel's Disable button.</summary>
    [ObservableProperty]
    public partial bool IsOffFromPanel { get; set; }

    [RelayCommand]
    private void Show()
    {
        _overlay.ToggleHidden();
    }

    [RelayCommand]
    private void TurnOn()
    {
        _overlay.TurnOn();
    }

    private void Refresh()
    {
        OverlaySettings current = _settings.Current;
        IsHidden = _overlay.IsHiddenByShortcut;
        HiddenText = _overlay.HideHotkeyError is null ? "Overlay hidden · Ctrl+Alt+H to show" : "Overlay hidden";
        IsOffFromPanel = !current.OverlayOn && current.OffFromPanel;
    }
}
