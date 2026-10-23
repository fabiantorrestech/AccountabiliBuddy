using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Today's deep work against the daily goal (redesign phase 4): the bar and text on Today, and the goal itself, changed
/// with the pencil (30 minutes to 8 hours). A goal is a target, not a block, so a change applies at once.
/// </summary>
public sealed partial class GoalCardViewModel : ObservableObject
{
    private readonly DeepWorkService _deepWork;
    private readonly GoalService _goal;

    public GoalCardViewModel(DeepWorkService deepWork, GoalService goal)
    {
        _deepWork = deepWork;
        _goal = goal;
        Text = string.Empty;
        Refresh();
        _deepWork.Changed += (_, _) => Refresh();
    }

    /// <summary>"40m of 1h", then "Goal met · 1h 05m".</summary>
    [ObservableProperty]
    public partial string Text { get; set; }

    /// <summary>0 to 100, for the bar.</summary>
    [ObservableProperty]
    public partial double Percent { get; set; }

    [ObservableProperty]
    public partial bool IsMet { get; set; }

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    [ObservableProperty]
    public partial decimal? GoalMinutes { get; set; }

    [RelayCommand]
    private void Edit()
    {
        GoalMinutes = _goal.Current.DailyMinutes;
        IsEditing = true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        await _goal.SetAsync((int)Math.Clamp(GoalMinutes ?? 60, 30, 480));
        IsEditing = false;
        _deepWork.CheckNow();
        Refresh();
    }

    [RelayCommand]
    private void Cancel()
    {
        IsEditing = false;
    }

    private void Refresh()
    {
        Text = _deepWork.Progress.Text;
        Percent = _deepWork.Progress.Fraction * 100;
        IsMet = _deepWork.Progress.Met;
    }
}
