using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// On Today after "No, a day off" in the Pre-Work window: says so, with Undo (it only makes this computer stricter
/// again, so no wait).
/// </summary>
public sealed partial class DayOffCardViewModel : ObservableObject
{
    private readonly PreWorkService _preWork;

    public DayOffCardViewModel(PreWorkService preWork)
    {
        _preWork = preWork;
        _preWork.Changed += (_, _) => IsVisible = _preWork.IsDayOff;
        IsVisible = _preWork.IsDayOff;
    }

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    [RelayCommand]
    private async Task UndoAsync()
    {
        await _preWork.CancelDayOffAsync();
        IsVisible = _preWork.IsDayOff;
    }
}
