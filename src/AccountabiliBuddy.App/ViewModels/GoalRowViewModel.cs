using AccountabiliBuddy.App.Services.Goals;
using AccountabiliBuddy.Core.Goals;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One goal row on the Goals list (round 2, brief §3.5, change note §4a): the icon on a chip, the name, "↳ target" under it ("Built
/// in" as a chip for Deep work, "↳ tracking only" when nothing is asked), the one number on the right, the kind's visual, and status
/// pills (amber Behind, a muted "Due Fri", Paused) in the number's slot. Paused goals (All only) are faded with "since Mon 10/12".
/// Kept between refreshes and updated in place (<see cref="Update"/>), so the row under the pointer or the keyboard never rebuilds.
/// </summary>
public sealed partial class GoalRowViewModel : ObservableObject
{
    private readonly Action<GoalRowViewModel> _select;

    public GoalRowViewModel(Guid id, Action<GoalRowViewModel> select)
    {
        Id = id;
        _select = select;
    }

    public Guid Id { get; }

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>"↳ 2h a day".</summary>
    [ObservableProperty]
    public partial string SubLine { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBuiltIn { get; set; }

    [ObservableProperty]
    public partial MaterialIconKind Icon { get; set; } = GoalIcons.Goals;

    /// <summary>The goal's colour (icon, visuals, heatmap); never text on its own.</summary>
    [ObservableProperty]
    public partial IBrush? ColourBrush { get; set; }

    [ObservableProperty]
    public partial string Number { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowsNumber { get; set; }

    [ObservableProperty]
    public partial string? Pill { get; set; }

    [ObservableProperty]
    public partial bool IsPillAmber { get; set; }

    [ObservableProperty]
    public partial bool IsPillMuted { get; set; }

    [ObservableProperty]
    public partial GoalRowVisual Visual { get; set; }

    [ObservableProperty]
    public partial double BarFraction { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<GoalStripCell>? Cells { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<double>? BarHeights { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<double?>? Trend { get; set; }

    [ObservableProperty]
    public partial bool IsPaused { get; set; }

    /// <summary>"since Mon 10/12" while paused.</summary>
    [ObservableProperty]
    public partial string? PausedSince { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Behind this period (the This week card's "N behind" jumps to the first one).</summary>
    [ObservableProperty]
    public partial bool IsBehind { get; set; }

    public bool ShowsBar => Visual == GoalRowVisual.Bar;

    public bool ShowsStrip => Visual == GoalRowVisual.Strip;

    public bool ShowsDots => Visual == GoalRowVisual.WeekDots;

    public bool ShowsDayBars => Visual == GoalRowVisual.DayBars;

    public bool ShowsTrend => Visual == GoalRowVisual.Sparkline;

    public bool HasPill => Pill is not null;

    /// <summary>The row's accessible name: "Deep work, 1h 10m / 2h" (or its pill).</summary>
    public string AccessibleName => $"{Name}, {(IsPillAmber ? Pill : Number)}";

    /// <summary>Takes this goal's numbers for today.</summary>
    public void Update(GoalFacts facts)
    {
        Goal goal = facts.Goal;
        Name = goal.Name;
        IsBuiltIn = goal.IsDeepWork;
        SubLine = "↳ " + GoalTexts.TargetLine(goal, facts.Today);
        Icon = GoalIcons.Of(goal.Icon);
        ColourBrush = GoalIcons.Brush(goal.Color);
        Visual = GoalNumbers.Visual(facts);
        Number = GoalNumbers.Number(facts);
        Pill = GoalNumbers.Pill(facts, out bool amber);
        IsPillAmber = amber;
        IsPillMuted = Pill is not null && !amber;
        // An amber pill takes the number's slot (change note §4a); a muted one sits beside it.
        ShowsNumber = !amber;
        IsBehind = facts.Status == GoalPeriodStatus.Behind && !facts.IsPaused;
        double fraction = GoalNumbers.Fraction(facts);
        BarFraction = double.IsNaN(fraction) ? 0 : fraction;
        IsPaused = facts.IsPaused;
        PausedSince = facts.PausedSince is DateOnly since ? "since " + GoalTexts.Date(since, facts.Today) : null;
        switch (Visual)
        {
            case GoalRowVisual.Strip:
                Cells = facts.Strip;
                BarHeights = null;
                break;
            case GoalRowVisual.WeekDots:
                Cells = facts.PeriodCells;
                BarHeights = null;
                break;
            case GoalRowVisual.DayBars:
            {
                List<GoalDayState> week = facts.Recent.TakeLast(7).ToList();
                Cells = week.Select(state => GoalStrip14.CellOf(goal, state, facts.Today)).ToList();
                double amount = goal.Target.Amount ?? 1;
                BarHeights = week.Select(state => amount > 0 ? state.Value / amount : 0).ToList();
                break;
            }

            default:
                Cells = null;
                BarHeights = null;
                break;
        }

        Trend = Visual == GoalRowVisual.Sparkline ? facts.Recent.Select(state => state.HasValue ? (double?)state.Value : null).ToList() : null;
        OnPropertyChanged(nameof(ShowsBar));
        OnPropertyChanged(nameof(ShowsStrip));
        OnPropertyChanged(nameof(ShowsDots));
        OnPropertyChanged(nameof(ShowsDayBars));
        OnPropertyChanged(nameof(ShowsTrend));
        OnPropertyChanged(nameof(HasPill));
        OnPropertyChanged(nameof(AccessibleName));
    }

    /// <summary>Shows this goal in the detail pane.</summary>
    [RelayCommand]
    private void Select()
    {
        _select(this);
    }
}
