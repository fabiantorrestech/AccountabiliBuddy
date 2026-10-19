using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Session tab button that hides or shows the on-screen timer for the running session. Works during focus, since
/// the timer is only a visual aid; it is on or off per profile in the Profiles tab.
/// </summary>
public sealed partial class TimerToggleViewModel : ObservableObject
{
    private readonly SessionOverlayService _overlay;

    public TimerToggleViewModel(SessionOverlayService overlay)
    {
        _overlay = overlay;
        ButtonText = string.Empty;
        _overlay.Changed += (_, _) => Refresh();
        Refresh();
    }

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    [ObservableProperty]
    public partial string ButtonText { get; set; }

    [RelayCommand]
    private void Toggle()
    {
        _overlay.SetHiddenForSession(!_overlay.IsHiddenForSession);
    }

    private void Refresh()
    {
        IsVisible = _overlay.IsOnForSession;
        ButtonText = _overlay.IsHiddenForSession ? "Show on-screen timer" : "Hide on-screen timer for this session";
    }
}
