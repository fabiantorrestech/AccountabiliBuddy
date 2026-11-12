using AccountabiliBuddy.App.Services.Goals;
using AccountabiliBuddy.Core.Goals;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The heatmap's day panel (round 2, change note §4a): a fixed slot that always shows one day, today by default; a clicked cell shows
/// that day with "Back to today". The date, "8 of 15 min", a status pill (Counted · ticked itself (teal), Not counted · 7 min short
/// (amber), Missed (amber), In progress · 7 min to go (grey)), the source line, and the actions: Tick it / Untick (a habit; an untick is
/// a hand entry of 0, the one untick encoding) and Edit (a value by hand). Back-filling a past day is allowed: it updates that week and
/// can rejoin a streak, and it never changes a guard decision already made (guards never read goal entries). Deep work takes no hand
/// entries: only its source line shows.
/// </summary>
public sealed partial class GoalDayPanelViewModel : ObservableObject
{
    private readonly Func<GoalEntry, Task<GoalChangeResult>> _save;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<Guid, string?> _labelName;
    private Goal? _goal;
    private DateOnly _today;

    /// <param name="save">Saves a hand entry (GoalsService.SaveEntryAsync).</param>
    /// <param name="labelName">A task label's name by id (the source line), or null.</param>
    public GoalDayPanelViewModel(Func<GoalEntry, Task<GoalChangeResult>> save, Func<DateTimeOffset> now, Func<Guid, string?> labelName)
    {
        _save = save;
        _now = now;
        _labelName = labelName;
    }

    [ObservableProperty]
    public partial DateOnly Day { get; set; }

    /// <summary>"Today · Wed 11/11", "Fri 10/30".</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Big { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Of { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsTeal { get; set; }

    [ObservableProperty]
    public partial bool IsAmber { get; set; }

    [ObservableProperty]
    public partial bool IsGrey { get; set; }

    [ObservableProperty]
    public partial string Source { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Note { get; set; }

    [ObservableProperty]
    public partial bool IsPast { get; set; }

    /// <summary>A habit can be ticked or unticked by hand.</summary>
    [ObservableProperty]
    public partial bool CanTick { get; set; }

    /// <summary>"Tick it now", "Tick it", "Untick".</summary>
    [ObservableProperty]
    public partial string TickText { get; set; } = "Tick it";

    /// <summary>Any goal but Deep work takes a value by hand.</summary>
    [ObservableProperty]
    public partial bool CanEdit { get; set; }

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    /// <summary>The value Edit saves (minutes for a time goal, the goal's unit otherwise).</summary>
    [ObservableProperty]
    public partial decimal? EditValue { get; set; }

    /// <summary>"Minutes to add by hand", "Value for Fri 10/30".</summary>
    [ObservableProperty]
    public partial string EditCaption { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Message { get; set; }

    public bool HasOf => !string.IsNullOrEmpty(Of);

    public bool HasNote => !string.IsNullOrEmpty(Note);

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    /// <summary>"Back to today" was pressed (the page puts the panel back on today).</summary>
    public event EventHandler? BackToTodayRequested;

    /// <summary>Shows <paramref name="state"/> of <paramref name="goal"/>.</summary>
    public void Show(Goal goal, GoalDayState state, DateOnly today)
    {
        bool sameDay = _goal?.Id == goal.Id && Day == state.Day;
        _goal = goal;
        _today = today;
        Day = state.Day;
        IsPast = state.Day != today;
        Title = IsPast ? GoalTexts.Date(state.Day, today) : $"Today · {GoalTexts.Date(today, today)}";
        if (!sameDay)
        {
            IsEditing = false;
            Message = null;
        }

        double? threshold = GoalTexts.Threshold(goal);
        GoalUnit unit = goal.Target.Unit;
        switch (goal.Kind)
        {
            case GoalKind.Habit when threshold is double minutes:
                Big = ((int)Math.Floor(state.Minutes)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                Of = $"of {GoalTexts.Minutes(minutes)}";
                break;
            case GoalKind.Habit:
                Big = state.Hit ? "Done" : "Not yet";
                Of = null;
                break;
            case GoalKind.Time:
                Big = GoalTexts.Minutes(state.Value);
                Of = goal.Target is { Period: GoalPeriod.Day, Amount: double amount } ? $"of {GoalTexts.Minutes(amount)}" : null;
                break;
            default:
                Big = state.HasValue ? GoalTexts.Value(state.Value, unit) : "–";
                Of = goal.Target is { Period: GoalPeriod.Day, Amount: double daily } ? $"of {GoalTexts.ShortValue(daily, unit)}" : null;
                break;
        }

        (Status, IsTeal, IsAmber, IsGrey) = StatusOf(goal, state, threshold);
        Source = SourceLine(goal, state);
        bool deepWork = goal.IsDeepWork;
        CanTick = !deepWork && goal.Kind == GoalKind.Habit && state.Status is not (GoalDayStatus.Paused or GoalDayStatus.NotDue);
        TickText = state.Hit ? "Untick" : IsPast ? "Tick it" : "Tick it now";
        CanEdit = !deepWork && goal.Kind != GoalKind.Habit && state.Status is not (GoalDayStatus.Paused or GoalDayStatus.NotDue);
        EditCaption = goal.Kind == GoalKind.Time ? $"Minutes to add by hand for {GoalTexts.Date(state.Day, today)}" : $"Value for {GoalTexts.Date(state.Day, today)}";
        Note = deepWork ? "Deep work counts focus only: no hand entries."
            : state.Status == GoalDayStatus.Paused ? "Paused days are neither hit nor missed."
            : !IsPast && threshold is double at ? $"It also ticks itself at {GoalTexts.Minutes(at)}."
            : state.Hit ? "Changes count toward that week and your streaks."
            : IsPast ? "Forgot to log it? Ticking counts the day and can rejoin your streak."
            : null;
        OnPropertyChanged(nameof(HasOf));
        OnPropertyChanged(nameof(HasNote));
        OnPropertyChanged(nameof(HasMessage));
    }

    [RelayCommand]
    private void BackToToday()
    {
        BackToTodayRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Tick it (a hand entry of 1) or Untick (a hand entry of 0, which wins over the time sources for that day).</summary>
    [RelayCommand]
    private async Task Tick()
    {
        if (_goal is not Goal goal || !CanTick)
        {
            return;
        }

        double value = TickText == "Untick" ? 0 : 1;
        await SaveAsync(goal, value);
    }

    [RelayCommand]
    private void Edit()
    {
        if (!CanEdit)
        {
            return;
        }

        IsEditing = !IsEditing;
        EditValue = null;
        Message = null;
        OnPropertyChanged(nameof(HasMessage));
    }

    [RelayCommand]
    private async Task SaveEdit()
    {
        if (_goal is not Goal goal || EditValue is not decimal value || value < 0)
        {
            return;
        }

        if (await SaveAsync(goal, (double)value))
        {
            IsEditing = false;
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
    }

    private async Task<bool> SaveAsync(Goal goal, double value)
    {
        var entry = new GoalEntry(Guid.NewGuid(), goal.Id, Day, null, value, GoalEntrySource.Hand, _now());
        GoalChangeResult result = await _save(entry);
        Message = result switch
        {
            GoalChangeResult.Applied or GoalChangeResult.Unchanged => null,
            GoalChangeResult.NotAvailable => "Goals aren't ready on this PC yet.",
            _ => "That can't be logged for this goal.",
        };
        OnPropertyChanged(nameof(HasMessage));
        return Message is null;
    }

    private static (string Text, bool Teal, bool Amber, bool Grey) StatusOf(Goal goal, GoalDayState state, double? threshold)
    {
        string Short(double gap) => goal.Kind == GoalKind.Habit || goal.Target.Unit == GoalUnit.Minutes ? GoalTexts.Minutes(gap) : GoalTexts.Value(gap, goal.Target.Unit);
        return state.Status switch
        {
            GoalDayStatus.CountedTickedItself => (state.Hit ? "Counted · ticked itself" : "Counted", true, false, false),
            GoalDayStatus.Counted => (state.ByHand ? "Counted · by hand" : "Counted", true, false, false),
            GoalDayStatus.NotCounted => ($"Not counted · {Short(state.Short)} short", false, true, false),
            GoalDayStatus.Missed => ("Missed", false, true, false),
            GoalDayStatus.InProgress => (state.Short > 0 ? $"In progress · {Short(state.Short)} to go" : "In progress", false, false, true),
            GoalDayStatus.Paused => ("Paused", false, false, true),
            _ => ("Not due", false, false, true),
        };
    }

    private string SourceLine(Goal goal, GoalDayState state)
    {
        string soFar = state.Day == _today ? " so far" : string.Empty;
        string minutes = GoalTexts.Minutes(state.Minutes);
        var parts = new List<string>();
        foreach (GoalSourceType source in state.Sources)
        {
            switch (source)
            {
                case GoalSourceType.FocusAll:
                    parts.Add($"{minutes} of counted focus{soFar}");
                    break;
                case GoalSourceType.FocusLabel:
                {
                    string? label = goal.Sources.Where(rule => rule.Type == GoalSourceType.FocusLabel && rule.LabelId is not null)
                        .Select(rule => _labelName(rule.LabelId!.Value)).OfType<string>().FirstOrDefault();
                    parts.Add(label is null ? $"{minutes} of focus on its label{soFar}" : $"{minutes} of focus on the {label} label{soFar}");
                    break;
                }

                case GoalSourceType.FocusTag:
                {
                    string? tag = goal.Sources.FirstOrDefault(rule => rule.Type == GoalSourceType.FocusTag)?.Tag;
                    parts.Add(tag is null ? $"{minutes} of focus on its tag{soFar}" : $"{minutes} of focus tagged #{tag.TrimStart('#')}{soFar}");
                    break;
                }

                case GoalSourceType.Hand:
                    parts.Add(goal.Kind == GoalKind.Habit ? (state.Hit ? "ticked by hand" : "unticked by hand") : "added by hand");
                    break;
                case GoalSourceType.CalendarCategory:
                    parts.Add("time from your calendar");
                    break;
                case GoalSourceType.HealthSteps or GoalSourceType.HealthWeight or GoalSourceType.HealthSleep:
                    parts.Add("from Health Connect");
                    break;
                case GoalSourceType.ScreenTime:
                    parts.Add("from screen time on your phone");
                    break;
                case GoalSourceType.Beeminder:
                    parts.Add("from Beeminder");
                    break;
            }
        }

        if (parts.Count > 0)
        {
            return char.ToUpperInvariant(parts[0][0]) + string.Join(" · ", parts)[1..];
        }

        return goal.IsDeepWork ? $"No counted focus{(state.Day == _today ? " yet" : string.Empty)}" : "Nothing logged for this day";
    }
}
