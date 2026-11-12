using AccountabiliBuddy.App.Services.Goals;
using AccountabiliBuddy.Core.Goals;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The Today sidebar's "Today's goals" (round 2 R5, replacing R2a's Deep-work-only hook): every live goal with Show on Today on and not
/// paused today, in the Goals list's order, each with the same number and status the Goals page shows (<see cref="GoalNumbers"/>). The
/// Deep work goal card (<see cref="DeepWork"/>) still gives the daily goal's edit and its lines (met, pace, phone, waiting messages), as
/// before. Before the goals are ready (or when they can't be read) it shows the Deep work card alone, as R2a did. Nothing here feeds a
/// guard.
/// </summary>
public sealed partial class TodayGoalsViewModel : ObservableObject
{
    private readonly GoalsService? _goals;
    private readonly GoalSourceDataService? _data;

    public TodayGoalsViewModel(GoalCardViewModel? deepWork, GoalsService? goals, GoalSourceDataService? data)
    {
        DeepWork = deepWork;
        _goals = goals;
        _data = data;
        if (_data is not null)
        {
            _data.Changed += (_, _) => Refresh();
        }

        Refresh();
    }

    /// <summary>Deep work's card alone (no goals on this PC: the R2a look).</summary>
    public static TodayGoalsViewModel CardOnly(GoalCardViewModel deepWork) => new(deepWork, null, null);

    private bool Ready => _goals is { Store.IsReady: true } && _data is not null;

    /// <summary>The Deep work goal card (daily goal edit, met, pace and waiting lines).</summary>
    public GoalCardViewModel? DeepWork { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRows), nameof(ShowsCardOnly))]
    public partial IReadOnlyList<TodayGoalRowViewModel> Rows { get; set; } = [];

    public bool HasRows => Rows.Count > 0;

    /// <summary>Goals aren't ready: the Deep work card's own row shows (the R2a look).</summary>
    public bool ShowsCardOnly => !Ready && DeepWork is not null;

    /// <summary>"No goals on Today. Turn on Show on Today in a goal's menu." when ready and none is on.</summary>
    public bool ShowsEmpty => Ready && Rows.Count == 0;

    /// <summary>Recomputes the rows (a goal, an entry or the focus changed).</summary>
    public void Refresh()
    {
        if (!Ready || _goals is null || _data is null)
        {
            Rows = [];
            OnPropertyChanged(nameof(ShowsEmpty));
            OnPropertyChanged(nameof(ShowsCardOnly));
            return;
        }

        DateOnly today = _data.Today;
        GoalWeekStart weekStart = _goals.Store.PageSettings.WeekStart;
        GoalSourceData data = _data.Current;
        Goal? deepWork = _goals.DeepWork;
        Rows = _goals.Store.Lists.LiveGoals
            .Where(goal => goal.ShowOnToday && !goal.IsPausedOn(today))
            .Select(goal => goal.IsDeepWork && deepWork is not null ? deepWork : goal)
            .Select(goal => Row(GoalFactsBuilder.Build(goal, data, today, weekStart)))
            .ToList();
        OnPropertyChanged(nameof(ShowsEmpty));
        OnPropertyChanged(nameof(ShowsCardOnly));
    }

    /// <summary>The goals shown (ids, list order), for the checks.</summary>
    public IReadOnlyList<Guid> ShownIds => Rows.Select(row => row.Id).ToList();

    private static TodayGoalRowViewModel Row(GoalFacts facts)
    {
        double fraction = GoalNumbers.Fraction(facts);
        GoalRowVisual visual = GoalNumbers.Visual(facts);
        double shownFraction = double.IsNaN(fraction)
            ? facts.Threshold is double threshold && threshold > 0 ? Math.Clamp(facts.TodayState.Minutes / threshold, 0, 1) : facts.TodayState.Hit ? 1 : 0
            : fraction;
        return new TodayGoalRowViewModel(
            facts.Goal.Id,
            facts.Goal.Name,
            GoalIcons.Of(facts.Goal.Icon),
            GoalIcons.Brush(facts.Goal.Color),
            GoalNumbers.Number(facts),
            shownFraction,
            visual != GoalRowVisual.Sparkline,
            facts.Status == GoalPeriodStatus.Behind,
            facts.Goal.IsDeepWork);
    }
}
