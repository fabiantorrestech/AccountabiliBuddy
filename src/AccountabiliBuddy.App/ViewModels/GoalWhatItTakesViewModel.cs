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

    /// <summary>"To hit 50h by Sun 1/31" (board Goals-PC); "What it takes" when the goal has no amount and date to name.</summary>
    [ObservableProperty]
    public partial string Head { get; set; } = "What it takes";

    /// <summary>"15h 50m to go · 81 days left".</summary>
    [ObservableProperty]
    public partial string Remaining { get; set; } = string.Empty;

    /// <summary>"Each day: about 12 min · each week: about 1h 25m" (one line, for the New goal box and screen readers).</summary>
    [ObservableProperty]
    public partial string? Each { get; set; }

    /// <summary>The labelled rows: Each day, Each week, Your pace.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<GoalWhatItTakesRow> Rows { get; set; } = [];

    /// <summary>"At this pace you hit 50h around Sun 1/10".</summary>
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
        string? target = goal.Target.TargetAmount is double amount ? Amount(amount) : null;
        Head = target is not null && goal.Target.TargetDate is DateOnly by ? $"To hit {target} by {GoalTexts.Date(by, today)}"
            : target is not null ? $"To hit {target}"
            : "What it takes";
        string left = $"{Amount(takes.Remaining)} to go";
        Remaining = takes.DaysLeft is int days ? $"{left} · {GoalTexts.Plural(days, "day")} left" : left;
        Each = takes.EachDay is double eachDay && takes.EachWeek is double eachWeek
            ? $"Each day: about {Amount(eachDay)} · each week: about {Amount(eachWeek)}"
            : null;
        var rows = new List<GoalWhatItTakesRow>();
        if (takes.EachDay is double perDayNeeded && takes.EachWeek is double perWeekNeeded)
        {
            rows.Add(new GoalWhatItTakesRow("Each day", $"about {Amount(perDayNeeded)}"));
            rows.Add(new GoalWhatItTakesRow("Each week", $"about {Amount(perWeekNeeded)}"));
        }

        if (takes.PacePerDay is double perDay)
        {
            rows.Add(new GoalWhatItTakesRow("Your pace", $"{Amount(perDay * 7)} a week"));
        }

        Rows = rows;
        Pace = takes.ProjectedFinish is DateOnly projected
            ? $"At this pace you hit {target ?? "it"} around {GoalTexts.Date(projected, today)}"
            : null;
        (Verdict, IsGreen, IsAmber) = takes.Verdict switch
        {
            GoalWhatItTakesVerdict.Met => ("Target met.", true, false),
            // Board Goals-PC: "Your 15 min a day already covers it" (the goal's own target line when it is a per-period one).
            GoalWhatItTakesVerdict.Covered => (GoalTexts.TargetLine(goal, today) is { } line && line.Contains(" a ", StringComparison.Ordinal)
                ? $"Your {line} already covers it"
                : "Your target already covers it", true, false),
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
