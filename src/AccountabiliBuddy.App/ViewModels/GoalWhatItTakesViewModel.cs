using AccountabiliBuddy.App.Services.Goals;
using AccountabiliBuddy.Core.Goals;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The Target tile's What it takes pop-up (round 2, brief §5.6, change note §4a): closed by default, opened and closed from the tile.
/// Remaining and days left, each day and each week (friendly values), your pace and the projected finish, then the verdict: green
/// "Your daily goal already covers it" or amber with how much more a day and a week; "The date has passed" past the date; and the
/// "Change the target" link (Edit goal › Target, R5b: <see cref="ChangeTargetRequested"/>).
/// </summary>
public sealed partial class GoalWhatItTakesViewModel : ObservableObject
{
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial bool IsAvailable { get; set; }

    /// <summary>"16h left · 81 days to Sun 1/31".</summary>
    [ObservableProperty]
    public partial string Remaining { get; set; } = string.Empty;

    /// <summary>"Each day: about 12 min · each week: 1h 25m".</summary>
    [ObservableProperty]
    public partial string? Each { get; set; }

    /// <summary>"Your pace: 14 min a day (4 weeks) · done ~Sun 1/10".</summary>
    [ObservableProperty]
    public partial string? Pace { get; set; }

    [ObservableProperty]
    public partial string Verdict { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsGreen { get; set; }

    [ObservableProperty]
    public partial bool IsAmber { get; set; }

    public bool HasEach => Each is not null;

    public bool HasPace => Pace is not null;

    /// <summary>R5b hook: "Change the target" opens Edit goal › Target.</summary>
    public event EventHandler? ChangeTargetRequested;

    /// <summary>Takes the maths for the goal shown (closed again when the goal changes).</summary>
    public void Update(GoalWhatItTakes? takes, Goal goal, DateOnly today, bool sameGoal)
    {
        IsAvailable = takes is not null;
        if (!sameGoal)
        {
            IsOpen = false;
        }

        if (takes is null)
        {
            return;
        }

        GoalUnit unit = goal.Target.Unit;
        string Amount(double value) => unit == GoalUnit.Minutes ? GoalWhatItTakes.MinutesText(GoalWhatItTakes.FriendlyMinutes(value)) : GoalTexts.Value(value, unit);
        string left = $"{Amount(takes.Remaining)} left";
        Remaining = takes.DaysLeft is int days && goal.Target.TargetDate is DateOnly date
            ? $"{left} · {GoalTexts.Plural(days, "day")} to {GoalTexts.Date(date, today)}"
            : left;
        Each = takes.EachDay is double eachDay && takes.EachWeek is double eachWeek
            ? $"Each day: about {Amount(eachDay)} · each week: {Amount(eachWeek)}"
            : null;
        string? pace = takes.PacePerDay is double perDay ? $"Your pace: {Amount(perDay)} a day ({GoalWhatItTakes.DefaultPaceWeeks} weeks)" : null;
        string? finish = takes.ProjectedFinish is DateOnly projected ? $"done ~{GoalTexts.Date(projected, today)}" : null;
        Pace = pace is null && finish is null ? null : string.Join(" · ", new[] { pace, finish }.OfType<string>());
        (Verdict, IsGreen, IsAmber) = takes.Verdict switch
        {
            GoalWhatItTakesVerdict.Met => ("Target met.", true, false),
            GoalWhatItTakesVerdict.Covered => ("Your daily goal already covers it.", true, false),
            GoalWhatItTakesVerdict.NeedsMore => ($"About {Amount(takes.MorePerDay ?? 0)} more a day, or {Amount(takes.MorePerWeek ?? 0)} more a week.", false, true),
            GoalWhatItTakesVerdict.DatePassed => ("The date has passed.", false, true),
            _ => ("No date set: this is the pace, not a deadline.", false, false),
        };
        OnPropertyChanged(nameof(HasEach));
        OnPropertyChanged(nameof(HasPace));
    }

    [RelayCommand]
    private void Toggle()
    {
        IsOpen = !IsOpen;
    }

    [RelayCommand]
    private void ChangeTarget()
    {
        IsOpen = false;
        ChangeTargetRequested?.Invoke(this, EventArgs.Empty);
    }
}
