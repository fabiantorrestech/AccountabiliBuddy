using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The shutdown routine's card on Today: a short stepped flow, one screen per step, each step skippable. "Next" writes that step's
/// choices at once (so a restart or "Not now" loses nothing already done), "Skip this step" writes nothing, and the closing screen
/// records "Work over for today". Steps with nothing to show (no parked thoughts, no unfinished tasks) are not shown and count as checked.
/// </summary>
public sealed partial class ShutdownCardViewModel : ObservableObject
{
    private readonly IReadOnlyList<ShutdownStep> _steps;
    private readonly ShutdownCardActions _actions;
    private readonly List<string> _checked;
    private bool _busy;

    /// <param name="steps">The steps to show, in order, ending with <see cref="ShutdownStep.Closing"/>.</param>
    /// <param name="alreadyChecked">What counts as checked without a screen (a step with nothing in it).</param>
    public ShutdownCardViewModel(
        IReadOnlyList<ShutdownStep> steps,
        IReadOnlyList<string> alreadyChecked,
        string closingLine,
        bool startedByHand,
        IReadOnlyList<ShutdownComebackRow> comebacks,
        IReadOnlyList<ShutdownTaskGroup> unfinished,
        IReadOnlyList<ShutdownLaterRow> laterDueSoon,
        IReadOnlyList<ShutdownAheadDay> calendarAhead,
        IReadOnlyList<ShutdownPlanRowViewModel> planRows,
        ShutdownReviewViewModel review,
        ShutdownCardActions actions,
        SessionTasksService? tasks = null)
    {
        _steps = steps;
        _checked = [.. alreadyChecked];
        ClosingLine = closingLine;
        StartedByHand = startedByHand;
        Comebacks = comebacks;
        UnfinishedGroups = unfinished;
        LaterDueSoon = laterDueSoon;
        CalendarAhead = calendarAhead;
        PlanRows = planRows;
        Review = review;
        _actions = actions;
        TomorrowPicker = tasks is null ? null : new ListPickerViewModel(tasks, tasks.Clock, FillTomorrowAsync);
        TomorrowTask = string.Empty;
        CheckedLines = [.. _checked];
    }

    // ---- The flow ----

    public IReadOnlyList<ShutdownStep> Steps => _steps;

    /// <summary>The index of the step on screen in <see cref="Steps"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Step), nameof(IsCapture), nameof(IsUnfinished), nameof(IsLookAhead), nameof(IsPlanTomorrow), nameof(IsReview), nameof(IsClosing),
        nameof(Title), nameof(Hint), nameof(StepText), nameof(CanSkip))]
    public partial int StepIndex { get; set; }

    public ShutdownStep Step => _steps[Math.Clamp(StepIndex, 0, _steps.Count - 1)];

    public bool IsCapture => Step == ShutdownStep.Capture;

    public bool IsUnfinished => Step == ShutdownStep.Unfinished;

    public bool IsLookAhead => Step == ShutdownStep.LookAhead;

    public bool IsPlanTomorrow => Step == ShutdownStep.PlanTomorrow;

    public bool IsReview => Step == ShutdownStep.Review;

    public bool IsClosing => Step == ShutdownStep.Closing;

    /// <summary>The card was started with "Start shutdown" (a manual start has no "Not now" return).</summary>
    public bool StartedByHand { get; }

    public string Title => Step switch
    {
        ShutdownStep.Capture => "Empty your capture",
        ShutdownStep.Unfinished => "Unfinished tasks",
        ShutdownStep.LookAhead => "Look ahead",
        ShutdownStep.PlanTomorrow => "Plan tomorrow",
        ShutdownStep.Review => "Today in review",
        _ => ClosingLine,
    };

    public string Hint => Step switch
    {
        ShutdownStep.Capture => "Give each parked thought a home, so it isn't left to percolate in your head.",
        ShutdownStep.Unfinished => "Tick what's done. The rest carries over to the next session's list.",
        ShutdownStep.LookAhead => "What's due soon and what's on your calendar. Read-only.",
        ShutdownStep.PlanTomorrow => "Pick up to three tasks and the part of tomorrow each belongs to.",
        ShutdownStep.Review => "How the day went. The feeling and note are optional and stay sealed on this PC.",
        _ => "Here is what you checked. Nothing is locked: you can still work if you want to. If work is still on, your start reminders keep working until it ends.",
    };

    /// <summary>E.g. "Step 2 of 4"; the closing screen counts as no step.</summary>
    public string StepText => IsClosing ? "Almost done" : $"Step {StepIndex + 1} of {_steps.Count - 1}";

    /// <summary>Every screen but the closing one can be skipped.</summary>
    public bool CanSkip => !IsClosing;

    /// <summary>What counts as checked so far, in the order it happened.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CheckedNothing))]
    public partial IReadOnlyList<string> CheckedLines { get; set; }

    /// <summary>Every step was skipped and none was empty, so nothing is listed.</summary>
    public bool CheckedNothing => CheckedLines.Count == 0;

    /// <summary>What the closing screen says (Settings › Your day).</summary>
    public string ClosingLine { get; }

    // ---- Empty your capture ----

    public IReadOnlyList<ShutdownComebackRow> Comebacks { get; }

    public int ComebackCount => Comebacks.Count;

    // ---- Unfinished tasks ----

    /// <summary>Today's unfinished tasks under their category, the ones with no category last.</summary>
    public IReadOnlyList<ShutdownTaskGroup> UnfinishedGroups { get; }

    public IEnumerable<ShutdownTaskRow> Unfinished => UnfinishedGroups.SelectMany(group => group.Rows);

    public int UnfinishedCount => Unfinished.Count();

    // ---- Look ahead ----

    public IReadOnlyList<ShutdownLaterRow> LaterDueSoon { get; }

    public bool HasLaterDueSoon => LaterDueSoon.Count > 0;

    public IReadOnlyList<ShutdownAheadDay> CalendarAhead { get; }

    public bool HasCalendarAhead => CalendarAhead.Count > 0;

    public bool NothingAhead => !HasLaterDueSoon && !HasCalendarAhead;

    // ---- Plan tomorrow ----

    /// <summary>Up to three tasks for tomorrow's parts; none when tomorrow's plan has no parts.</summary>
    public IReadOnlyList<ShutdownPlanRowViewModel> PlanRows { get; }

    public bool HasPlanRows => PlanRows.Count > 0;

    public bool HasNoPlanParts => PlanRows.Count == 0;

    /// <summary>Tomorrow's first task, the quick path: it waits at the top of Today's checklist.</summary>
    [ObservableProperty]
    public partial string TomorrowTask { get; set; }

    /// <summary>"From your lists" by the box; null without the tasks service.</summary>
    public ListPickerViewModel? TomorrowPicker { get; }

    /// <summary>The list task chosen for tomorrow's first: it moves there on Next. Null for a typed one; changing the text makes it typed.</summary>
    public (PulledListKind Kind, Guid Id)? TomorrowSource { get; private set; }

    private string? _tomorrowSourceTitle;

    partial void OnTomorrowTaskChanged(string value)
    {
        if (TomorrowSource is not null && value.Trim() != _tomorrowSourceTitle)
        {
            TomorrowSource = null;
            _tomorrowSourceTitle = null;
        }
    }

    private Task FillTomorrowAsync(PulledListKind kind, Guid id, string title)
    {
        _tomorrowSourceTitle = title.Trim();
        TomorrowPicker?.WillFill(title);
        TomorrowTask = title;
        TomorrowSource = (kind, id);
        return Task.CompletedTask;
    }

    // ---- Today in review ----

    public ShutdownReviewViewModel Review { get; }

    // ---- Commands ----

    /// <summary>Writes this step's choices and goes on.</summary>
    [RelayCommand]
    private async Task NextAsync()
    {
        if (_busy || IsClosing)
        {
            return;
        }

        _busy = true;
        try
        {
            await _actions.Apply(Step, this);
            AddChecked(CheckedName(Step));
            GoOn();
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Leaves this step as it is: nothing is written and it isn't listed as checked.</summary>
    [RelayCommand]
    private void SkipStep()
    {
        if (!_busy && !IsClosing)
        {
            GoOn();
        }
    }

    /// <summary>"Work over for today": records it and closes the card.</summary>
    [RelayCommand]
    private async Task FinishAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            await _actions.Finish(this);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Closes the card; it comes back once (not for a card started by hand).</summary>
    [RelayCommand]
    private Task NotNowAsync()
    {
        return _actions.NotNow();
    }

    /// <summary>Opens the Plan page, where tomorrow's parts are edited.</summary>
    [RelayCommand]
    private void OpenPlan()
    {
        _actions.OpenPlan();
    }

    /// <summary>The checked lines as the closing screen and Today list them.</summary>
    public IReadOnlyList<string> Checked => _checked;

    private void GoOn()
    {
        if (StepIndex < _steps.Count - 1)
        {
            StepIndex++;
        }
    }

    private void AddChecked(string name)
    {
        if (!_checked.Contains(name))
        {
            _checked.Add(name);
            CheckedLines = [.. _checked];
        }
    }

    private static string CheckedName(ShutdownStep step) => step switch
    {
        ShutdownStep.Capture => "Emptied your capture",
        ShutdownStep.Unfinished => "Sorted unfinished tasks",
        ShutdownStep.LookAhead => "Looked ahead",
        ShutdownStep.PlanTomorrow => "Planned tomorrow",
        ShutdownStep.Review => "Reviewed today",
        _ => string.Empty,
    };
}
