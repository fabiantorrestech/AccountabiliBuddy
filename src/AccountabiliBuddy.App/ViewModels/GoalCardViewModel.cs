using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Engine;
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
        // The Budget pace moves with the clock even when no session runs: once a minute.
        var paceTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        paceTimer.Tick += (_, _) => RefreshPace();
        paceTimer.Start();
    }

    /// <summary>
    /// How today's Budget is going (ADR 47); null when today isn't a Budget day. Set by the app, from the day plan, the
    /// counted focus and the clock.
    /// </summary>
    public Func<BudgetPace?>? PaceNow { get; set; }

    /// <summary>"2h 10m left · 5h of day left"; empty on a day that isn't a Budget day.</summary>
    [ObservableProperty]
    public partial string PaceText { get; set; } = string.Empty;

    /// <summary>The pace line shows on track (plain).</summary>
    [ObservableProperty]
    public partial bool IsPaceOnTrack { get; set; }

    /// <summary>The pace line shows, in amber: the day can no longer hold the budget left.</summary>
    [ObservableProperty]
    public partial bool IsPaceBehind { get; set; }

    /// <summary>Raised when the pace line was read again (the overlay's goal line follows).</summary>
    public event EventHandler? PaceRefreshed;

    /// <summary>"Phone-free: 2 sessions today · 3-day streak" (ADR 26); empty when no phone counts.</summary>
    [ObservableProperty]
    public partial string PhoneText { get; set; } = string.Empty;

    /// <summary>The phone-free line; set by the app.</summary>
    public Func<string>? PhoneLine { get; set; }

    /// <summary>"40m of 1h", then "Goal met · 1h 05m".</summary>
    [ObservableProperty]
    public partial string Text { get; set; }

    /// <summary>0 to 100, for the bar.</summary>
    [ObservableProperty]
    public partial double Percent { get; set; }

    /// <summary>0 to 1, for the ring on Today.</summary>
    [ObservableProperty]
    public partial double Fraction { get; set; }

    /// <summary>The ring's big number, e.g. "1h 10m".</summary>
    [ObservableProperty]
    public partial string FocusedText { get; set; } = string.Empty;

    /// <summary>The line under it, e.g. "of 2h today".</summary>
    [ObservableProperty]
    public partial string OfGoalText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsMet { get; set; }

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    [ObservableProperty]
    public partial decimal? GoalMinutes { get; set; }

    /// <summary>Why a change wasn't saved; null when it was.</summary>
    [ObservableProperty]
    public partial string? Message { get; set; }

    /// <summary>True while lowering the goal has to wait (the work-hours guard has work left today: earned time).</summary>
    public Func<bool>? LowerWaits { get; init; }

    [RelayCommand]
    private void Edit()
    {
        GoalMinutes = _goal.Current.DailyMinutes;
        IsEditing = true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        int minutes = (int)Math.Clamp(GoalMinutes ?? 60, 30, 480);
        if (minutes < _goal.Current.DailyMinutes && LowerWaits?.Invoke() == true)
        {
            Message = "Lowering the goal waits until today's work hours are over (raising it is fine).";
            return;
        }

        await _goal.SetAsync(minutes);
        Message = null;
        IsEditing = false;
        _deepWork.CheckNow();
        Refresh();
    }

    [RelayCommand]
    private void Cancel()
    {
        Message = null;
        IsEditing = false;
    }

    private void Refresh()
    {
        Text = _deepWork.Progress.Text;
        Percent = _deepWork.Progress.Fraction * 100;
        Fraction = _deepWork.Progress.Fraction;
        FocusedText = GoalProgress.Duration(_deepWork.Progress.Focused);
        OfGoalText = OfGoal(_deepWork.Progress);
        IsMet = _deepWork.Progress.Met;
        RefreshPhone();
        RefreshPace();
    }

    // "of 2h today · 50 min to go"; once the goal is met, just "of 2h today".
    private static string OfGoal(GoalProgress progress)
    {
        string text = $"of {GoalProgress.Duration(progress.Goal)} today";
        if (progress.Met)
        {
            return text;
        }

        TimeSpan left = progress.Goal - progress.Focused;
        string toGo = left.TotalMinutes < 60 ? $"{(int)Math.Ceiling(left.TotalMinutes)} min" : GoalProgress.Duration(left);
        return $"{text} · {toGo} to go";
    }

    /// <summary>Reads the Budget pace line again (a minute passed, or the plan or the focus changed).</summary>
    public void RefreshPace()
    {
        BudgetPace? pace = PaceNow?.Invoke();
        PaceText = pace is null ? string.Empty : BudgetPaceText.Describe(pace);
        IsPaceBehind = pace?.Behind == true;
        IsPaceOnTrack = pace is not null && !IsPaceBehind;
        PaceRefreshed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reads the phone-free line again (a phone's file arrived).</summary>
    public void RefreshPhone()
    {
        PhoneText = PhoneLine?.Invoke() ?? string.Empty;
    }
}
