using AccountabiliBuddy.App.Services.Goals;
using AccountabiliBuddy.Core.Goals;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The goal dialogs' Target section and New goal's step 2 (round 2 R5.4, brief §4.4 "Target step", §5.6, §5.7): how often (Daily / Weekly /
/// Every 2 weeks / Monthly), the amount with its unit and direction (a habit: ticks itself at N minutes, or N times a period), "on N days",
/// the optional long-term target (Reach … By …), the live What it takes box and the footnote about raising and lowering. For Deep work the
/// daily and weekly minutes (its targets go through <see cref="GoalsService"/>'s waiting rule) with each waiting change and Cancel.
/// Nothing is saved here: the dialog reads <see cref="BuildTarget"/> when it saves.
/// </summary>
public sealed partial class GoalTargetEditorViewModel : ObservableObject
{
    private static readonly GoalPeriod[] Periods = [GoalPeriod.Day, GoalPeriod.Week, GoalPeriod.TwoWeeks, GoalPeriod.Month];
    private static readonly GoalUnit[] AmountUnits = [GoalUnit.Count, GoalUnit.Steps, GoalUnit.Lb, GoalUnit.Kg];
    private static readonly GoalDirection[] Directions = [GoalDirection.AtLeast, GoalDirection.AtMost, GoalDirection.Reach];

    private readonly Func<DeepWorkTargetKind, Task>? _cancelPending;
    private GoalKind _kind = GoalKind.Habit;
    // A habit keeps the unit it was stored with (a tick, or a count of days hit for its long-term target).
    private GoalUnit _habitUnit = GoalUnit.YesNo;
    private bool _loading;

    public GoalTargetEditorViewModel(bool productaBuddy, Func<DeepWorkTargetKind, Task>? cancelPending = null)
    {
        _cancelPending = cancelPending;
        IsProductaBuddy = productaBuddy;
        WhatItTakes = new GoalWhatItTakesViewModel();
    }

    /// <summary>Raised on every change the dialog redraws from (the nav line, What it takes, Create now).</summary>
    public event EventHandler? Changed;

    public GoalWhatItTakesViewModel WhatItTakes { get; }

    public bool IsProductaBuddy { get; }

    public IReadOnlyList<string> PeriodChoices { get; } = ["Daily", "Weekly", "Every 2 weeks", "Monthly"];

    public IReadOnlyList<string> UnitChoices { get; } = ["Count", "Steps", "lb", "kg"];

    public IReadOnlyList<string> DirectionChoices { get; } = ["At least", "At most", "Reach"];

    [ObservableProperty]
    public partial int PeriodIndex { get; set; }

    /// <summary>The amount per period: minutes (time), times (a weekly habit), a value (amount).</summary>
    [ObservableProperty]
    public partial decimal? Amount { get; set; }

    [ObservableProperty]
    public partial int UnitIndex { get; set; }

    [ObservableProperty]
    public partial int DirectionIndex { get; set; }

    /// <summary>A habit's "ticks itself at N min" of its time sources; null = ticked by hand only.</summary>
    [ObservableProperty]
    public partial decimal? ThresholdMinutes { get; set; }

    /// <summary>"On N days" of a weekly / monthly amount or time goal; null = the period's total counts.</summary>
    [ObservableProperty]
    public partial decimal? WeeklyDays { get; set; }

    /// <summary>The long-term target (hours for a time goal, the unit otherwise); null = none.</summary>
    [ObservableProperty]
    public partial decimal? TargetAmount { get; set; }

    [ObservableProperty]
    public partial DateTime? TargetDate { get; set; }

    // ---- Deep work (its targets go through GoalsService) ----

    [ObservableProperty]
    public partial bool IsDeepWork { get; set; }

    [ObservableProperty]
    public partial decimal? DailyMinutes { get; set; }

    /// <summary>Deep work's weekly target in hours; null = none.</summary>
    [ObservableProperty]
    public partial decimal? WeeklyHours { get; set; }

    [ObservableProperty]
    public partial bool TrackTimeOnly { get; set; }

    [ObservableProperty]
    public partial string? DailyPending { get; set; }

    [ObservableProperty]
    public partial string? WeeklyPending { get; set; }

    [ObservableProperty]
    public partial string? TrackTimeOnlyPending { get; set; }

    /// <summary>Why the target can't be saved yet; null when it can.</summary>
    [ObservableProperty]
    public partial string? Problem { get; set; }

    public bool HasDailyPending => DailyPending is not null;

    public bool HasWeeklyPending => WeeklyPending is not null;

    public bool HasTrackTimeOnlyPending => TrackTimeOnlyPending is not null;

    public bool HasProblem => Problem is not null;

    public GoalPeriod Period => Periods[Math.Clamp(PeriodIndex, 0, Periods.Length - 1)];

    public GoalKind Kind => _kind;

    public bool IsHabit => _kind == GoalKind.Habit && !IsDeepWork;

    public bool IsDailyHabit => IsHabit && Period == GoalPeriod.Day;

    public bool ShowsTimesPerPeriod => IsHabit && Period != GoalPeriod.Day;

    public bool ShowsAmount => !IsDeepWork && _kind is GoalKind.Time or GoalKind.Amount;

    public bool ShowsUnit => !IsDeepWork && _kind == GoalKind.Amount;

    public bool ShowsDirection => !IsDeepWork && _kind is GoalKind.Time or GoalKind.Amount;

    public bool ShowsWeeklyDays => ShowsAmount && Period != GoalPeriod.Day;

    public bool ShowsLongTerm => !IsDeepWork;

    public bool IsNotDeepWork => !IsDeepWork;

    /// <summary>"Each day", "Each week", "Every 2 weeks", "Each month".</summary>
    public string AmountCaption => Period switch
    {
        GoalPeriod.Day => "Each day",
        GoalPeriod.Week => "Each week",
        GoalPeriod.TwoWeeks => "Every 2 weeks",
        _ => "Each month",
    };

    /// <summary>"min", "steps", "lb"...</summary>
    public string AmountUnitText => _kind == GoalKind.Time ? "min" : UnitText(Unit);

    /// <summary>"h" for a time goal, "days" (days hit) for a habit, the unit otherwise.</summary>
    public string TargetUnitText => _kind == GoalKind.Time ? "h" : _kind == GoalKind.Habit ? "days" : UnitText(Unit);

    public GoalUnit Unit => _kind switch
    {
        GoalKind.Time => GoalUnit.Minutes,
        GoalKind.Habit => _habitUnit,
        GoalKind.Milestone => GoalUnit.Count,
        _ => AmountUnits[Math.Clamp(UnitIndex, 0, AmountUnits.Length - 1)],
    };

    public GoalDirection Direction => ShowsDirection ? Directions[Math.Clamp(DirectionIndex, 0, Directions.Length - 1)] : GoalDirection.AtLeast;

    /// <summary>The footnote under the target (brief §4.4): raising applies at once, lowering waits only for Deep work (§10 Q6).</summary>
    public string Footnote => IsDeepWork
        ? IsProductaBuddy
            ? "Raising a target applies at once. In ProductaBuddy mode lowering one applies at once too, except while today's work hours still have work left."
            : "Raising a target applies at once. Lowering one waits (10 min by default) and never happens during a session; Track time only waits the same way."
        : "Raising or lowering this goal's target applies at once: it feeds no guard. Only Deep work's targets wait (10 min by default) and never change during a session; in PB they apply at once.";

    /// <summary>Loads a goal's target (Deep work: from <paramref name="deepWork"/>'s view and settings).</summary>
    public void Load(Goal goal, DeepWorkGoalSettings? deepWork, DateOnly today, TimeZoneInfo zone)
    {
        _loading = true;
        try
        {
            _kind = goal.Kind;
            _habitUnit = goal.Kind == GoalKind.Habit && goal.Target.Unit is GoalUnit.YesNo or GoalUnit.Count ? goal.Target.Unit : GoalUnit.YesNo;
            IsDeepWork = goal.IsDeepWork;
            GoalTarget target = goal.Target;
            PeriodIndex = Math.Max(0, Array.IndexOf(Periods, target.Period));
            UnitIndex = Math.Max(0, Array.IndexOf(AmountUnits, target.Unit));
            DirectionIndex = Math.Max(0, Array.IndexOf(Directions, target.Direction));
            ThresholdMinutes = GoalTexts.Threshold(goal) is double threshold ? (decimal)threshold : null;
            Amount = target.Amount is double amount ? (decimal)amount : null;
            WeeklyDays = target.WeeklyDays;
            TargetAmount = target.TargetAmount is double longTerm ? (decimal)(goal.Kind == GoalKind.Time ? longTerm / 60 : longTerm) : null;
            TargetDate = target.TargetDate?.ToDateTime(TimeOnly.MinValue);
            if (goal.IsDeepWork)
            {
                DailyMinutes = target.Amount is double daily ? (decimal)daily : null;
                WeeklyHours = goal.SecondTarget?.Amount is double weekly ? Math.Round((decimal)weekly / 60, 2) : null;
                TrackTimeOnly = deepWork?.TrackTimeOnly == true;
                DailyPending = PendingText(deepWork?.PendingDaily, "daily", zone);
                WeeklyPending = PendingText(deepWork?.PendingWeekly, "weekly", zone);
                TrackTimeOnlyPending = deepWork?.PendingTrackTimeOnly is GoalPendingChange track
                    ? $"Track time only waits until {GoalTexts.Time(track.AppliesAt, zone)}."
                    : null;
            }
        }
        finally
        {
            _loading = false;
        }

        RaiseShape();
        Validate();
    }

    /// <summary>
    /// The weekly review's "Adjust the target" (round 2 R5.4, brief §5.8): its suggested numbers pre-filled over the goal's own (a moved
    /// date, one day fewer, a lower amount; Deep work's daily minutes). Nothing is saved until Save, and a lower Deep work target still waits.
    /// </summary>
    public void ApplySuggestion(GoalReviewAdjustment adjustment)
    {
        if (adjustment.TargetDate is DateOnly date)
        {
            TargetDate = date.ToDateTime(TimeOnly.MinValue);
        }

        if (adjustment.WeeklyDays is int days)
        {
            WeeklyDays = days;
        }

        if (adjustment.Amount is double amount)
        {
            if (IsDeepWork)
            {
                DailyMinutes = (decimal)amount;
            }
            else
            {
                Amount = (decimal)amount;
            }
        }
    }

    /// <summary>The kind changed on Basics: the fields that make sense for it show.</summary>
    public void SetKind(GoalKind kind)
    {
        if (_kind == kind)
        {
            return;
        }

        _kind = kind;
        if (kind == GoalKind.Habit && Amount is null && Period != GoalPeriod.Day)
        {
            Amount = 3;
        }

        RaiseShape();
        Validate();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The target to save (null when <see cref="Problem"/> says why not). Deep work's targets aren't built here.</summary>
    public GoalTarget? BuildTarget()
    {
        Validate();
        if (Problem is not null)
        {
            return null;
        }

        GoalPeriod period = Period;
        double? amount = _kind switch
        {
            GoalKind.Habit => period == GoalPeriod.Day ? null : (double)(Amount ?? 1),
            GoalKind.Time or GoalKind.Amount => Amount is decimal value ? (double)value : null,
            _ => Amount is decimal milestone ? (double)milestone : null,
        };
        double? longTerm = TargetAmount is decimal target ? (double)(_kind == GoalKind.Time ? target * 60 : target) : null;
        int? days = ShowsWeeklyDays && WeeklyDays is decimal weekly ? (int)weekly : null;
        return new GoalTarget(
            period,
            amount,
            Unit,
            Direction,
            longTerm,
            longTerm is not null && TargetDate is DateTime date ? DateOnly.FromDateTime(date) : null,
            days);
    }

    /// <summary>The habit's threshold for its time sources (null = by hand only).</summary>
    public double? Threshold => IsHabit && ThresholdMinutes is decimal minutes && minutes > 0 ? (double)minutes : null;

    /// <summary>Deep work's daily minutes to ask for (30 to 480), null when Track time only stays on.</summary>
    public int? DeepWorkDaily => DailyMinutes is decimal minutes ? (int)Math.Round(minutes) : null;

    /// <summary>Deep work's weekly target in minutes (null = none).</summary>
    public int? DeepWorkWeekly => WeeklyHours is decimal hours && hours > 0 ? (int)Math.Round(hours * 60) : null;

    /// <summary>"15 min a day · 50h by 1/31": the nav line under Target.</summary>
    public string Summary(Goal draft, DateOnly today)
    {
        if (IsDeepWork)
        {
            if (TrackTimeOnly)
            {
                return "Tracking only";
            }

            string daily = DeepWorkDaily is int minutes ? $"{GoalTexts.Minutes(minutes)} a day" : "no daily target";
            return DeepWorkWeekly is int weekly ? $"{daily} · {GoalTexts.Minutes(weekly)} a week" : daily;
        }

        string line = GoalTexts.TargetLine(draft, today);
        return draft.Target.TargetAmount is double target && draft.Target.TargetDate is DateOnly date && draft.Kind != GoalKind.Amount
            ? $"{line} · {(draft.Kind == GoalKind.Habit ? GoalTexts.Number(target, 0) + " days" : GoalTexts.ShortValue(target, draft.Target.Unit))} by {date.Month}/{date.Day}"
            : line;
    }

    [RelayCommand]
    private async Task CancelPending(DeepWorkTargetKind kind)
    {
        if (_cancelPending is not null)
        {
            await _cancelPending(kind);
        }

        switch (kind)
        {
            case DeepWorkTargetKind.Daily:
                DailyPending = null;
                break;
            case DeepWorkTargetKind.Weekly:
                WeeklyPending = null;
                break;
            default:
                TrackTimeOnlyPending = null;
                break;
        }
    }

    partial void OnPeriodIndexChanged(int value) => Edited(shape: true);

    partial void OnAmountChanged(decimal? value) => Edited();

    partial void OnUnitIndexChanged(int value) => Edited(shape: true);

    partial void OnDirectionIndexChanged(int value) => Edited();

    partial void OnThresholdMinutesChanged(decimal? value) => Edited();

    partial void OnWeeklyDaysChanged(decimal? value) => Edited();

    partial void OnTargetAmountChanged(decimal? value) => Edited();

    partial void OnTargetDateChanged(DateTime? value) => Edited();

    partial void OnDailyMinutesChanged(decimal? value) => Edited();

    partial void OnWeeklyHoursChanged(decimal? value) => Edited();

    partial void OnDailyPendingChanged(string? value) => OnPropertyChanged(nameof(HasDailyPending));

    partial void OnWeeklyPendingChanged(string? value) => OnPropertyChanged(nameof(HasWeeklyPending));

    partial void OnTrackTimeOnlyPendingChanged(string? value) => OnPropertyChanged(nameof(HasTrackTimeOnlyPending));

    partial void OnProblemChanged(string? value) => OnPropertyChanged(nameof(HasProblem));

    partial void OnIsDeepWorkChanged(bool value) => RaiseShape();

    private void Edited(bool shape = false)
    {
        if (_loading)
        {
            return;
        }

        if (shape)
        {
            RaiseShape();
        }

        Validate();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Says why the target can't be saved: the first thing to fix.
    private void Validate()
    {
        Problem = FindProblem();
    }

    private string? FindProblem()
    {
        if (IsDeepWork)
        {
            if (!TrackTimeOnly && DeepWorkDaily is not int daily)
            {
                return "Set a daily target (30 min to 8h), or use Track time only from the goal's menu.";
            }

            if (DeepWorkDaily is int minutes && (minutes < GoalsService.MinDailyMinutes || minutes > GoalsService.MaxDailyMinutes))
            {
                return "The daily target is 30 min to 8h.";
            }

            if (DeepWorkWeekly is int weekly && (weekly < GoalsService.MinWeeklyMinutes || weekly > GoalsService.MaxWeeklyMinutes))
            {
                return "The weekly target is 30 min to 56h.";
            }

            return null;
        }

        if (ShowsTimesPerPeriod && (Amount is not decimal times || times < 1 || times > 31))
        {
            return "How many times? 1 to 31.";
        }

        if (ShowsAmount && Amount is decimal amount && amount <= 0)
        {
            return "The amount is more than 0.";
        }

        if (ShowsAmount && Amount is null && TargetAmount is null)
        {
            return "Set an amount for each period, a long-term target, or both.";
        }

        if (ShowsWeeklyDays && WeeklyDays is decimal days && (days < 1 || days > (Period == GoalPeriod.Month ? 31 : Period == GoalPeriod.TwoWeeks ? 14 : 7)))
        {
            return "The number of days doesn't fit the period.";
        }

        if (TargetAmount is decimal target && target <= 0)
        {
            return "The long-term target is more than 0.";
        }

        if (TargetDate is not null && TargetAmount is null)
        {
            return "A date needs a long-term amount to reach.";
        }

        if (IsHabit && ThresholdMinutes is decimal threshold && (threshold < 1 || threshold > 1440))
        {
            return "Ticks itself at 1 to 1,440 min.";
        }

        return null;
    }

    private void RaiseShape()
    {
        foreach (string name in new[]
        {
            nameof(Period), nameof(IsHabit), nameof(IsDailyHabit), nameof(ShowsTimesPerPeriod), nameof(ShowsAmount), nameof(ShowsUnit),
            nameof(ShowsDirection), nameof(ShowsWeeklyDays), nameof(ShowsLongTerm), nameof(IsNotDeepWork), nameof(AmountCaption),
            nameof(AmountUnitText), nameof(TargetUnitText), nameof(Unit), nameof(Footnote), nameof(Kind),
        })
        {
            OnPropertyChanged(name);
        }
    }

    private static string UnitText(GoalUnit unit) => unit switch
    {
        GoalUnit.Steps => "steps",
        GoalUnit.Lb => "lb",
        GoalUnit.Kg => "kg",
        GoalUnit.Minutes => "min",
        _ => "times",
    };

    // "Lowers to 1h 30m at 2:14 PM" with Cancel (brief §5.7).
    private static string? PendingText(GoalPendingChange? pending, string which, TimeZoneInfo zone)
    {
        if (pending is null)
        {
            return null;
        }

        string to = pending.Amount is double amount ? GoalTexts.Minutes(amount) : "no target";
        return $"The {which} target lowers to {to} at {GoalTexts.Time(pending.AppliesAt, zone)}.";
    }
}
