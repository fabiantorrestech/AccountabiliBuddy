using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The Setup DP Assistant (ADR 47 follow-up): seven steps (workdays, work time, deep work goal, lunch, sleep, weeks, review)
/// that set up the day plan with sensible defaults, each prefilled from what the plan and settings already say, and re-runnable.
/// Nothing is saved until the review's Save. The plan goes through <see cref="ConfigurationService"/> (a change that frees
/// today's work waits there, and the review says so), the goal through <see cref="GoalService"/> with the goal card's own
/// lowering rule, and sleep and lunch length through their own services. Locked while a focus session the user started runs.
/// </summary>
public sealed partial class DpAssistantViewModel : ObservableObject
{
    private const int MinGoalMinutes = 30;
    private const int MaxGoalMinutes = 480;

    private readonly ConfigurationService _configuration;
    private readonly SleepSettingsService _sleep;
    private readonly GoalService _goal;
    private readonly SessionController _sessions;
    private readonly DpAssistantService _state;
    private readonly LunchService _lunch;
    private readonly List<DpTemplateChoice> _templates = new();

    // True while the pages are filled from the plan, so that isn't taken as an edit.
    private bool _loading;

    // The workdays the Weeks grid last took over from the Workdays page.
    private string? _syncedWorkdays;

    public DpAssistantViewModel(
        ConfigurationService configuration, SleepSettingsService sleep, GoalService goal, SessionController sessions,
        DpAssistantService state, LunchService lunch)
    {
        _configuration = configuration;
        _sleep = sleep;
        _goal = goal;
        _sessions = sessions;
        _state = state;
        _lunch = lunch;
        foreach (DayOfWeek day in DayPlan.MondayFirst)
        {
            Days.Add(new WorkdayChip(day, isOn: false));
            WeekRows.Add(new DpWeekRowViewModel(day));
        }

        _sessions.StateChanged += (_, _) => IsLocked = _sessions.IsEnforcing;
        IsLocked = _sessions.IsEnforcing;
        Load();
    }

    /// <summary>Today (the trusted clock); set by the app.</summary>
    public Func<DateOnly> Today { get; init; } = () => DateOnly.FromDateTime(DateTime.Now);

    /// <summary>True while lowering the goal has to wait (work left today under the work-hours guard); set by the app.</summary>
    public Func<bool> GoalLowerWaits { get; init; } = () => false;

    /// <summary>This PC guards work hours (the goal lifts the guard once met); set by the app.</summary>
    public Func<bool> WorkHoursGuardOn { get; init; } = () => false;

    /// <summary>A phone is linked, so the smart bedtime can work (only it measures sleep); set by the app.</summary>
    public Func<bool> HasLinkedPhone { get; init; } = () => false;

    /// <summary>Read today's progress again after the goal changed; set by the app.</summary>
    public Action RefreshProgress { get; init; } = () => { };

    /// <summary>Raised when the window should close.</summary>
    public event EventHandler? CloseRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsWorkdays), nameof(IsWorkTime), nameof(IsDeepWork), nameof(IsLunch), nameof(IsSleep), nameof(IsWeeks), nameof(IsReview), nameof(IsDone),
        nameof(StepTitle), nameof(StepNumberText), nameof(NextText))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand), nameof(NextCommand))]
    public partial DpStep Step { get; set; }

    [ObservableProperty]
    public partial bool IsLocked { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BackCommand), nameof(NextCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>What went wrong on this step, or what happened on the last save; null when there is nothing to say.</summary>
    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    public bool IsWorkdays => Step == DpStep.Workdays;

    public bool IsWorkTime => Step == DpStep.WorkTime;

    public bool IsDeepWork => Step == DpStep.DeepWork;

    public bool IsLunch => Step == DpStep.Lunch;

    public bool IsSleep => Step == DpStep.Sleep;

    public bool IsWeeks => Step == DpStep.Weeks;

    public bool IsReview => Step == DpStep.Review;

    public bool IsDone => Step == DpStep.Done;

    public string StepTitle => Step switch
    {
        DpStep.Workdays => "Your workdays",
        DpStep.WorkTime => "Work time",
        DpStep.DeepWork => "Deep work goal",
        DpStep.Lunch => "Lunch",
        DpStep.Sleep => "Sleep",
        DpStep.Weeks => "Weeks",
        DpStep.Review => "Review",
        _ => "Saved",
    };

    public string StepNumberText => Step == DpStep.Done ? "Done" : $"Step {(int)Step + 1} of {(int)DpStep.Review + 1}";

    public string NextText => Step switch
    {
        DpStep.Review => "Save",
        DpStep.Done => "Close",
        _ => "Next",
    };

    private bool CanGoBack => !IsBusy && Step != DpStep.Workdays && Step != DpStep.Done;

    private bool CanGoNext => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        StatusMessage = null;
        Step = Step - 1;
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextAsync()
    {
        StatusMessage = null;
        switch (Step)
        {
            case DpStep.Review:
                await SaveAsync();
                break;
            case DpStep.Done:
                CloseRequested?.Invoke(this, EventArgs.Empty);
                break;
            default:
                if (Problem(Step) is string problem)
                {
                    StatusMessage = problem;
                    return;
                }

                Step = Step + 1;
                OnEntered(Step);
                break;
        }
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    partial void OnStepChanged(DpStep value)
    {
        // Going back to a page doesn't redo anything; Review and the Weeks grid are worked out when entered (OnEntered).
        if (value == DpStep.Review)
        {
            BuildReview();
        }
    }

    private void OnEntered(DpStep step)
    {
        if (step == DpStep.Weeks)
        {
            TakeWorkdaysIntoWeeks();
        }
    }

    // ---- 1. Workdays ----

    /// <summary>Monday first; on = the day follows the Workday type in week 1. Any days can be picked.</summary>
    public ObservableCollection<WorkdayChip> Days { get; } = new();

    // ---- 2. Work time ----

    /// <summary>0 = a work window, 1 = a budget of hours, 2 = keep the Workday type's own timeline.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorkWindow), nameof(IsBudget), nameof(IsKeepTimeline))]
    public partial int WorkChoice { get; set; }

    /// <summary>The Workday type has a timeline of its own with work in it, so keeping it is an option.</summary>
    [ObservableProperty]
    public partial bool HasTimeline { get; set; }

    [ObservableProperty]
    public partial TimeSpan? WindowStart { get; set; } = new TimeSpan(9, 0, 0);

    [ObservableProperty]
    public partial TimeSpan? WindowEnd { get; set; } = new TimeSpan(17, 0, 0);

    [ObservableProperty]
    public partial decimal? BudgetHours { get; set; } = 6;

    public bool IsWorkWindow
    {
        get => WorkChoice == 0;
        set
        {
            if (value)
            {
                WorkChoice = 0;
            }
        }
    }

    public bool IsBudget
    {
        get => WorkChoice == 1;
        set
        {
            if (value)
            {
                WorkChoice = 1;
            }
        }
    }

    public bool IsKeepTimeline
    {
        get => WorkChoice == 2;
        set
        {
            if (value)
            {
                WorkChoice = 2;
            }
        }
    }

    // ---- 3. Deep work goal ----

    /// <summary>The daily goal in hours, 0.5 to 8 in half hours.</summary>
    [ObservableProperty]
    public partial decimal? GoalHours { get; set; } = 1;

    /// <summary>What deep work asks of a person, shown under the question (Cal Newport).</summary>
    public string GoalGuidance =>
        "If you're not used to deep work, start low: 1 hour is a good first goal. Cal Newport notes that most professionals top out "
        + "around 4 to 4.5 hours a day of deep, non-context-switching work, and only after a lot of deliberate practice.";

    // ---- 4. Lunch ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFixedLunch))]
    public partial bool IsLunchWindow { get; set; } = true;

    public bool IsFixedLunch
    {
        get => !IsLunchWindow;
        set => IsLunchWindow = !value;
    }

    [ObservableProperty]
    public partial TimeSpan? LunchWindowStart { get; set; } = new TimeSpan(11, 30, 0);

    [ObservableProperty]
    public partial TimeSpan? LunchWindowEnd { get; set; } = new TimeSpan(14, 0, 0);

    /// <summary>How long a lunch window's lunch lasts once started, in minutes (15 to 120).</summary>
    [ObservableProperty]
    public partial decimal? LunchMinutes { get; set; } = 60;

    [ObservableProperty]
    public partial TimeSpan? LunchHint { get; set; } = new TimeSpan(12, 0, 0);

    [ObservableProperty]
    public partial TimeSpan? FixedLunchStart { get; set; } = new TimeSpan(12, 0, 0);

    [ObservableProperty]
    public partial TimeSpan? FixedLunchEnd { get; set; } = new TimeSpan(13, 0, 0);

    // ---- 5. Sleep ----

    /// <summary>Sleep is set up here (false = skipped: the plan's nights stay and sleep mode goes off).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSleepSkipped))]
    public partial bool SetUpSleep { get; set; } = true;

    public bool IsSleepSkipped
    {
        get => !SetUpSleep;
        set => SetUpSleep = !value;
    }

    [ObservableProperty]
    public partial TimeSpan? WorkdayBedtime { get; set; } = new TimeSpan(22, 0, 0);

    [ObservableProperty]
    public partial TimeSpan? OtherBedtime { get; set; } = new TimeSpan(23, 30, 0);

    /// <summary>0 = the phone's alarm, 1 = this PC's alarm, 2 = a fixed wake time.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWakePhone), nameof(IsWakePc), nameof(IsWakeFixed))]
    public partial int WakeChoice { get; set; }

    [ObservableProperty]
    public partial TimeSpan? WorkdayWake { get; set; } = new TimeSpan(6, 0, 0);

    [ObservableProperty]
    public partial TimeSpan? OtherWake { get; set; } = new TimeSpan(7, 30, 0);

    public bool IsWakePhone
    {
        get => WakeChoice == 0;
        set
        {
            if (value)
            {
                WakeChoice = 0;
            }
        }
    }

    public bool IsWakePc
    {
        get => WakeChoice == 1;
        set
        {
            if (value)
            {
                WakeChoice = 1;
            }
        }
    }

    public bool IsWakeFixed
    {
        get => WakeChoice == 2;
        set
        {
            if (value)
            {
                WakeChoice = 2;
            }
        }
    }

    [ObservableProperty]
    public partial bool SmartWorkdays { get; set; }

    [ObservableProperty]
    public partial bool SmartOffDays { get; set; }

    /// <summary>The smart bedtime needs the linked phone, which measures sleep.</summary>
    public bool CanUseSmartBedtime => HasLinkedPhone();

    public string SmartBedtimeHint => CanUseSmartBedtime
        ? "Your phone learns it from your measured sleep and moves bedtime earlier as you earn it."
        : "Needs your linked phone, which measures your sleep. Link it in Devices; the choice is kept meanwhile.";

    // ---- 6. Weeks ----

    /// <summary>Monday first: the day type each weekday follows, in each week of the cycle.</summary>
    public ObservableCollection<DpWeekRowViewModel> WeekRows { get; } = new();

    /// <summary>The day types a cell can pick.</summary>
    public IReadOnlyList<DpTemplateChoice> TemplateChoices => _templates;

    /// <summary>The grid's column headings: "Every week", or "Week 1", "Week 2"…</summary>
    public ObservableCollection<string> WeekHeaders { get; } = new();

    /// <summary>0 = the same every week, 1 = alternating A/B, 2 = a repeating cycle of 2 to 8 weeks.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSameWeeks), nameof(IsAlternatingWeeks), nameof(IsCustomWeeks), nameof(HasSeveralWeeks))]
    public partial int WeekMode { get; set; }

    [ObservableProperty]
    public partial decimal? CustomWeekCount { get; set; } = 3;

    /// <summary>Which week of the cycle this week is (1-based).</summary>
    [ObservableProperty]
    public partial int ThisWeek { get; set; } = 1;

    /// <summary>1 to the number of weeks, for "This week is week …".</summary>
    public ObservableCollection<int> ThisWeekChoices { get; } = new();

    public bool HasSeveralWeeks => WeekMode != 0;

    public bool IsSameWeeks
    {
        get => WeekMode == 0;
        set
        {
            if (value)
            {
                WeekMode = 0;
            }
        }
    }

    public bool IsAlternatingWeeks
    {
        get => WeekMode == 1;
        set
        {
            if (value)
            {
                WeekMode = 1;
            }
        }
    }

    public bool IsCustomWeeks
    {
        get => WeekMode == 2;
        set
        {
            if (value)
            {
                WeekMode = 2;
            }
        }
    }

    partial void OnWeekModeChanged(int value)
    {
        ResizeWeeks();
    }

    partial void OnCustomWeekCountChanged(decimal? value)
    {
        if (WeekMode == 2)
        {
            ResizeWeeks();
        }
    }

    private int WeekCount => WeekMode switch
    {
        0 => 1,
        1 => 2,
        _ => (int)Math.Clamp(CustomWeekCount ?? 3, 2, PlanCycle.MaxWeeks),
    };

    // Adds weeks as copies of the last one, or drops the last ones, so the grid has WeekCount columns.
    private void ResizeWeeks()
    {
        if (_loading)
        {
            return;
        }

        int count = WeekCount;
        foreach (DpWeekRowViewModel row in WeekRows)
        {
            while (row.Cells.Count < count)
            {
                row.Cells.Add(new DpWeekCellViewModel { Template = row.Cells.Count > 0 ? row.Cells[^1].Template : OffDayChoice });
            }

            while (row.Cells.Count > count)
            {
                row.Cells.RemoveAt(row.Cells.Count - 1);
            }
        }

        RefreshWeekHeaders(count);
    }

    private void RefreshWeekHeaders(int count)
    {
        WeekHeaders.Clear();
        ThisWeekChoices.Clear();
        for (int number = 1; number <= count; number++)
        {
            WeekHeaders.Add(count == 1 ? "Every week" : $"Week {number}");
            ThisWeekChoices.Add(number);
        }

        ThisWeek = Math.Clamp(ThisWeek, 1, count);
    }

    private DpTemplateChoice? WorkdayChoice => _templates.FirstOrDefault(choice => choice.Id == DayPlan.WorkdayTemplateId);

    private DpTemplateChoice? OffDayChoice => _templates.FirstOrDefault(choice => choice.Id == DayPlan.OffDayTemplateId);

    // Week 1 follows the Workdays page: a weekday ticked there follows Workday; one that follows Workday and isn't ticked goes to
    // Off day; one following another day type stays unless ticked (the classic view's rule, DayPlan.WithWorkdays). Only
    // when the ticks changed since the grid last took them, so edits made in the grid itself aren't undone.
    private void TakeWorkdaysIntoWeeks()
    {
        string signature = string.Join(",", Days.Where(chip => chip.IsOn).Select(chip => chip.Day));
        if (signature == _syncedWorkdays)
        {
            return;
        }

        _syncedWorkdays = signature;
        foreach (DpWeekRowViewModel row in WeekRows)
        {
            bool on = Days.First(chip => chip.Day == row.Day).IsOn;
            DpWeekCellViewModel first = row.Cells[0];
            first.Template = on
                ? WorkdayChoice ?? first.Template
                : first.Template?.Id == DayPlan.WorkdayTemplateId ? OffDayChoice : first.Template;
        }
    }

    // ---- 7. Review ----

    /// <summary>How the buddy behaves through each kind of day under these answers.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<DpReviewDay> ReviewDays { get; set; } = Array.Empty<DpReviewDay>();

    /// <summary>What to know before saving: a problem, what waits, what stays, what is skipped.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> ReviewNotes { get; set; } = Array.Empty<string>();

    /// <summary>The answers can't be saved (the review says why); Save is off until Back fixes them.</summary>
    [ObservableProperty]
    public partial bool HasProblem { get; set; }

    /// <summary>After Save: what was saved and what is waiting.</summary>
    [ObservableProperty]
    public partial string SavedText { get; set; } = string.Empty;

    // ---- loading ----

    private void Load()
    {
        _loading = true;
        DayPlan plan = _configuration.Editing.DayPlan;
        SleepSettings sleep = _sleep.Current;
        DpAssistantPlanChoices choices = DpAssistantPlanBuilder.Prefill(plan, sleep.Weeknights, sleep.Weekends);

        _templates.Clear();
        _templates.AddRange(plan.Templates.Select(template => new DpTemplateChoice(template.Id, template.Name)));
        OnPropertyChanged(nameof(TemplateChoices));

        // 1. Workdays: week 1's Workday cells; Monday to Friday when the plan has no workday at all.
        HashSet<DayOfWeek> workdays = plan.Workdays.ToHashSet();
        if (workdays.Count == 0)
        {
            workdays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];
        }

        foreach (WorkdayChip chip in Days)
        {
            chip.IsOn = workdays.Contains(chip.Day);
        }

        // 2. Work time.
        HasTimeline = DpAssistantPlanBuilder.HasTimeline(plan, plan.Workday);
        WorkChoice = choices.WorkTime switch { DpWorkTime.Budget => 1, DpWorkTime.KeepTimeline => 2, _ => 0 };
        WindowStart = choices.WindowStart.ToTimeSpan();
        WindowEnd = choices.WindowEnd.ToTimeSpan();
        BudgetHours = (decimal)(choices.BudgetMinutes / 60.0);

        // 3. Goal.
        GoalHours = Math.Clamp(_goal.Current.DailyMinutes, MinGoalMinutes, MaxGoalMinutes) / 60m;

        // 4. Lunch. A flexible lunch's length is the lunch setting's (it wins over the part's own).
        IsLunchWindow = choices.Lunch == DpLunchKind.Flexible;
        LunchWindowStart = choices.LunchWindowStart.ToTimeSpan();
        LunchWindowEnd = choices.LunchWindowEnd.ToTimeSpan();
        LunchMinutes = (int)_lunch.Length.TotalMinutes;
        LunchHint = choices.LunchHint.ToTimeSpan();
        FixedLunchStart = choices.FixedLunchStart.ToTimeSpan();
        FixedLunchEnd = choices.FixedLunchEnd.ToTimeSpan();

        // 5. Sleep.
        SetUpSleep = sleep.Enabled;
        if (choices.WorkdayNight is SleepTimes workNight)
        {
            WorkdayBedtime = workNight.Bedtime.ToTimeSpan();
            WorkdayWake = workNight.Wake.ToTimeSpan();
        }

        if (choices.OtherNight is SleepTimes otherNight)
        {
            OtherBedtime = otherNight.Bedtime.ToTimeSpan();
            OtherWake = otherNight.Wake.ToTimeSpan();
        }

        WakeChoice = choices.WorkdayNight?.WakeByAlarm == true ? (sleep.UsePcAlarm ? 1 : 0) : 2;
        SmartWorkdays = sleep.SmartWorkdays;
        SmartOffDays = sleep.SmartOffDays;

        // 6. Weeks.
        int weekCount = plan.Cycle.Weeks.Count;
        WeekMode = weekCount == 1 ? 0 : weekCount == 2 ? 1 : 2;
        CustomWeekCount = Math.Max(weekCount, 2);
        foreach (DpWeekRowViewModel row in WeekRows)
        {
            row.Cells.Clear();
            foreach (IReadOnlyList<WeekdayTemplate> week in plan.Cycle.Weeks)
            {
                Guid id = week.FirstOrDefault(entry => entry.Day == row.Day)?.TemplateId ?? DayPlan.OffDayTemplateId;
                row.Cells.Add(new DpWeekCellViewModel { Template = _templates.FirstOrDefault(choice => choice.Id == id) ?? OffDayChoice });
            }
        }

        RefreshWeekHeaders(weekCount);
        ThisWeek = Math.Clamp(plan.Cycle.WeekNumberFor(Today()), 1, weekCount);
        _syncedWorkdays = string.Join(",", Days.Where(chip => chip.IsOn).Select(chip => chip.Day));
        _loading = false;
    }

    // ---- checks ----

    // What is wrong with the answers on a page, or null.
    private string? Problem(DpStep step)
    {
        switch (step)
        {
            case DpStep.WorkTime when IsWorkWindow:
                if (WindowStart is not TimeSpan start || WindowEnd is not TimeSpan end)
                {
                    return "Pick when work starts and ends.";
                }

                return end != TimeSpan.Zero && end <= start ? "The work window must end after it starts (12:00 AM counts as midnight)." : null;
            case DpStep.WorkTime when IsBudget:
                return BudgetHours is not >= 0.25m ? "A budget is at least 15 minutes." : null;
            case DpStep.DeepWork:
                return GoalHours is null ? "Pick a goal between 30 minutes and 8 hours." : null;
            case DpStep.Lunch when IsLunchWindow:
                if (LunchWindowStart is not TimeSpan windowStart || LunchWindowEnd is not TimeSpan windowEnd || LunchMinutes is null || LunchHint is null)
                {
                    return "Fill in the lunch window, its length and the hint time.";
                }

                return windowEnd <= windowStart ? "The lunch window must end after it starts." : null;
            case DpStep.Lunch:
                if (FixedLunchStart is not TimeSpan fixedStart || FixedLunchEnd is not TimeSpan fixedEnd)
                {
                    return "Pick when lunch starts and ends.";
                }

                return fixedEnd - fixedStart < TimeSpan.FromMinutes(15) ? "A fixed lunch lasts at least 15 minutes and ends the same day." : null;
            case DpStep.Sleep when SetUpSleep:
                return WorkdayBedtime is null || OtherBedtime is null || (IsWakeFixed && (WorkdayWake is null || OtherWake is null))
                    ? "Fill in the bedtimes and wake times."
                    : null;
            default:
                return null;
        }
    }

    // ---- building ----

    private DpAssistantPlanChoices BuildChoices()
    {
        DayPlan plan = _configuration.Editing.DayPlan;
        int count = WeekCount;
        var weeks = new List<IReadOnlyList<WeekdayTemplate>>();
        for (int index = 0; index < count; index++)
        {
            weeks.Add(WeekRows
                .Where(row => row.Cells[index].Template is not null)
                .Select(row => new WeekdayTemplate(row.Day, row.Cells[index].Template!.Id))
                .ToList());
        }

        // One week: the start makes no difference, so the plan's stays. Several: week 1 is placed so this week is ThisWeek.
        DateOnly startsOn = count == 1 || (count == plan.Cycle.Weeks.Count && plan.Cycle.WeekNumberFor(Today()) == ThisWeek)
            ? plan.Cycle.StartsOn
            : WeekSwitch.MondayOf(Today()).AddDays(-7 * (ThisWeek - 1));
        TimeOnly Time(TimeSpan? value, TimeOnly fallback) => value is TimeSpan span ? TimeOnly.FromTimeSpan(span) : fallback;
        TimeOnly lunchStart = Time(LunchWindowStart, DpAssistantPlanChoices.DefaultLunchWindow.Start);
        return new DpAssistantPlanChoices(
            IsKeepTimeline ? DpWorkTime.KeepTimeline : IsBudget ? DpWorkTime.Budget : DpWorkTime.Window,
            Time(WindowStart, DpAssistantPlanChoices.DefaultWindow.Start),
            Time(WindowEnd, DpAssistantPlanChoices.DefaultWindow.End),
            (int)Math.Round((double)(BudgetHours ?? 6) * 60),
            IsLunchWindow ? DpLunchKind.Flexible : DpLunchKind.Fixed,
            lunchStart,
            Time(LunchWindowEnd, DpAssistantPlanChoices.DefaultLunchWindow.End),
            (int)Math.Clamp(LunchMinutes ?? 60, LunchService.MinLengthMinutes, LunchService.MaxLengthMinutes),
            Time(LunchHint, lunchStart),
            Time(FixedLunchStart, DpAssistantPlanChoices.DefaultFixedLunch.Start),
            Time(FixedLunchEnd, DpAssistantPlanChoices.DefaultFixedLunch.End),
            SetUpSleep ? Night(WorkdayBedtime, WorkdayWake) : null,
            SetUpSleep ? Night(OtherBedtime, OtherWake) : null,
            weeks,
            startsOn);
    }

    // A night from a bedtime and a wake time; woken by an alarm, the end is bedtime plus the sleep goal (how a Sleep part keeps it).
    private SleepTimes Night(TimeSpan? bedtime, TimeSpan? wake)
    {
        TimeOnly bed = TimeOnly.FromTimeSpan(bedtime ?? TimeSpan.FromHours(22));
        return IsWakeFixed
            ? new SleepTimes(bed, TimeOnly.FromTimeSpan(wake ?? TimeSpan.FromHours(6)))
            : new SleepTimes(bed, bed.Add(_sleep.Current.Goal), WakeByAlarm: true);
    }

    private int GoalMinutes => (int)Math.Clamp(Math.Round((GoalHours ?? 1) * 2, MidpointRounding.AwayFromZero) * 30, MinGoalMinutes, MaxGoalMinutes);

    private SleepSettings BuildSleepSettings()
    {
        SleepSettings now = _sleep.Current;
        return SetUpSleep
            ? now with
            {
                Enabled = true,
                UsePhoneAlarm = WakeChoice == 0,
                UsePcAlarm = WakeChoice == 1,
                SmartWorkdays = SmartWorkdays,
                SmartOffDays = SmartOffDays,
            }
            : now with { Enabled = false };
    }

    private void BuildReview()
    {
        DayPlan current = _configuration.Current.DayPlan;
        DayPlan before = _configuration.Editing.DayPlan;
        DpAssistantPlanChoices choices = BuildChoices();
        DayPlan proposed = DpAssistantPlanBuilder.Apply(before, choices);
        SleepSettings sleep = BuildSleepSettings();
        var notes = new List<string>();

        string? problem = DayPlanValidator.Validate(proposed);
        HasProblem = problem is not null;
        if (problem is not null)
        {
            notes.Add($"Can't be saved yet: {problem} Press Back and change it.");
        }

        if (_configuration.DayPlanHold?.Invoke(current, proposed) is string held)
        {
            int minutes = (int)_configuration.Current.Locks.LooseningDelay.TotalMinutes;
            notes.Add($"Held for {minutes} minutes: {held}. Your current plan keeps applying until then; a banner counts it down and can cancel it.");
        }

        if (GoalMinutes < _goal.Current.DailyMinutes && GoalLowerWaits())
        {
            notes.Add($"The goal stays at {DpAssistantReview.Hours(_goal.Current.DailyMinutes)} until today's work hours are over: lowering it waits (raising it applies at once).");
        }

        if (choices.WorkTime != DpWorkTime.KeepTimeline && before.Workday.Parts.Any(part => before.Category(part.CategoryId)?.Behavior.IsWork() == true))
        {
            notes.Add("The work and Comms parts of your Workday timeline are replaced by this, so those timed parts are gone. Lunch, Pre-Work and sleep stay.");
        }

        if (!SetUpSleep)
        {
            notes.Add("Sleep is skipped, so sleep mode goes off: bedtime reminders, the sleep line on the day bar and the wake-up alarms won't work.");
        }

        if (choices.Weeks.Count > 1 && choices.Weeks.Any(week => week.Any(entry => entry.TemplateId != DayPlan.WorkdayTemplateId && entry.TemplateId != DayPlan.OffDayTemplateId)))
        {
            notes.Add("Day types other than Workday and Off day keep their own times; edit them in the Day plan tab.");
        }

        ReviewNotes = notes;
        ReviewDays = DpAssistantReview.Describe(
            proposed,
            Today(),
            new DpReviewContext(
                GoalMinutes,
                sleep.Enabled,
                sleep.Reminders,
                sleep.UsePcAlarm ? "this PC's alarm" : "your phone's alarm",
                WorkHoursGuardOn(),
                SetUpSleep && (SmartWorkdays || SmartOffDays),
                date => sleep.FallbackNightBefore(date)));
    }

    // ---- saving ----

    private async Task SaveAsync()
    {
        if (_sessions.IsEnforcing)
        {
            StatusMessage = "Locked while a focus session you started is running. Try again after it.";
            return;
        }

        if (HasProblem)
        {
            StatusMessage = "Press Back and fix what the review says first.";
            return;
        }

        IsBusy = true;
        try
        {
            DpAssistantPlanChoices choices = BuildChoices();
            await _configuration.UpdateAsync(document => document with { DayPlan = DpAssistantPlanBuilder.Apply(document.DayPlan, choices) });
            if (_configuration.IsReadOnly)
            {
                StatusMessage = "Not saved: settings are read-only (see the banner).";
                return;
            }

            var saved = new List<string> { _configuration.DayPlanWaitText ?? "Your day plan is saved." };
            if (GoalMinutes != _goal.Current.DailyMinutes)
            {
                if (GoalMinutes < _goal.Current.DailyMinutes && GoalLowerWaits())
                {
                    saved.Add($"The goal stays at {DpAssistantReview.Hours(_goal.Current.DailyMinutes)} until today's work hours are over.");
                }
                else
                {
                    await _goal.SetAsync(GoalMinutes);
                    RefreshProgress();
                    saved.Add($"The daily goal is {DpAssistantReview.Hours(GoalMinutes)}.");
                }
            }

            if (choices.Lunch == DpLunchKind.Flexible)
            {
                await _lunch.SetLengthAsync(choices.LunchMinutes);
            }

            // After the plan, whose change re-derives the nights from it; the wake source, smart bedtime and on/off are the user's own.
            SleepSettings sleep = BuildSleepSettings();
            if (sleep != _sleep.Current)
            {
                await _sleep.SaveAsync(sleep);
            }

            await _state.MarkRunAsync();
            SavedText = string.Join(" ", saved);
            StatusMessage = null;
            Step = DpStep.Done;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
