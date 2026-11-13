using AccountabiliBuddy.App.Services.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The one-time card on Today and on the Tasks page after the task migration (MIG1): "N tasks from the earlier version are in
/// To sort · Sort them now · ×". Sort them now opens Tasks (To sort is the Shelf's first column); × closes it for good. Never
/// shown during a session.
/// </summary>
public sealed partial class EarlierTasksCardViewModel : ObservableObject
{
    private readonly EarlierTasksNoticeStore _notice;
    private readonly Func<bool> _isSessionActive;
    private readonly Action _openTasks;

    public EarlierTasksCardViewModel(EarlierTasksNoticeStore notice, Func<bool> isSessionActive, Action openTasks)
    {
        _notice = notice;
        _isSessionActive = isSessionActive;
        _openTasks = openTasks;
        _notice.Changed += (_, _) => Refresh();
        Refresh();
    }

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    /// <summary>Re-reads the count and whether a session runs (call when either may have changed).</summary>
    public void Refresh()
    {
        int count = _notice.Count;
        Text = count == 1 ? "1 task from the earlier version is in To sort." : $"{count} tasks from the earlier version are in To sort.";
        IsVisible = count > 0 && !_isSessionActive();
    }

    [RelayCommand]
    private void SortThemNow() => _openTasks();

    [RelayCommand]
    private async Task Dismiss() => await _notice.DismissAsync();
}
