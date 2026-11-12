using System.Globalization;
using AccountabiliBuddy.App.Services.Goals;
using AccountabiliBuddy.Core.Goals;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The weekly goals review dialog (round 2 R5.4; brief §4.4 "Weekly goals review dialog", §5.8; board Goals-PC-Review): "Weekly goals
/// review", "3 of 8 · week of Nov 8 – 14 · ↑/↓ and Enter, or 1–4"; the goal card (icon, name, "175 lb by Mar 1 · Fitness",
/// "← 3 / 8 →", the facts the suggestion used as pills, amber when behind); the numbered decisions 1 Keep going, 2 Adjust the target
/// (opens Edit goal › Target with the suggested numbers; lowering Deep work still waits), 3 Pause this goal (not Deep work), 4 Decide
/// next week, with "Suggested" and "Your choice"; "Stop for now" (Esc) keeps the answers; "Then the tidy-up · about 2 minutes" when
/// Settings › Goals runs the tidy-up straight after, which then follows the last answer. Each answer is kept at once
/// (<see cref="GoalReviewService.DecideAsync"/>) and the next unanswered goal shows. A session that starts closes it (never during a
/// session). Goal names are the user's text: shown only.
/// </summary>
public sealed partial class GoalReviewViewModel : ObservableObject
{
    private readonly GoalReviewService _review;
    private readonly IReadOnlyList<Goal> _goals;
    private readonly DateOnly _today;
    private bool _closed;

    public GoalReviewViewModel(GoalReviewService review)
    {
        _review = review;
        _today = review.Data.Today;
        Week = review.WeekOf(review.DueAt(review.Now));
        _goals = review.GoalsToReview(_today);
        Sub = PositionText = GoalName = GoalLine = FinishedText = string.Empty;
        GoalIconBrush = Brushes.Transparent;
        Decisions =
        [
            new(GoalReviewChoice.KeepGoing, 1, "Keep going", MaterialIconKind.Check, new AsyncRelayCommand(() => ChooseAsync(GoalReviewChoice.KeepGoing))),
            new(GoalReviewChoice.Adjust, 2, "Adjust the target", MaterialIconKind.PencilOutline, new AsyncRelayCommand(() => ChooseAsync(GoalReviewChoice.Adjust))),
            new(GoalReviewChoice.Pause, 3, "Pause this goal", MaterialIconKind.Pause, new AsyncRelayCommand(() => ChooseAsync(GoalReviewChoice.Pause))),
            new(GoalReviewChoice.DecideNextWeek, 4, "Decide next week", MaterialIconKind.SkipNext, new AsyncRelayCommand(() => ChooseAsync(GoalReviewChoice.DecideNextWeek))),
        ];
        _review.Changed += OnReviewChanged;
        Index = Math.Max(0, FirstUnanswered(0) ?? 0);
        Load();
    }

    /// <summary>Asks the dialog to close.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>"Adjust the target": opens Edit goal › Target for the goal with the suggested numbers (set by the app; awaited).</summary>
    public Func<Guid, GoalReviewAdjustment?, Task>? AdjustTarget { get; set; }

    /// <summary>The first day of the week reviewed.</summary>
    public DateOnly Week { get; }

    public IReadOnlyList<GoalReviewDecisionViewModel> Decisions { get; }

    public int Total => _goals.Count;

    /// <summary>The review was answered to the end and the tidy-up is due straight after: the app runs it once this closes.</summary>
    public bool ThenTidyUp { get; private set; }

    /// <summary>"Then the tidy-up · about 2 minutes" shows (Settings › Goals' option is on).</summary>
    public bool ShowsTidyUpNote => _review.TidyUpFollows;

    public string TidyUpNote => "Then the tidy-up · about 2 minutes";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BackCommand), nameof(ForwardCommand))]
    public partial int Index { get; set; }

    /// <summary>"3 of 8 · week of Nov 8 – 14 · ↑/↓ and Enter, or 1–4".</summary>
    [ObservableProperty]
    public partial string Sub { get; set; }

    /// <summary>"3 / 8".</summary>
    [ObservableProperty]
    public partial string PositionText { get; set; }

    [ObservableProperty]
    public partial string GoalName { get; set; }

    /// <summary>"175 lb by Mar 1 · Fitness".</summary>
    [ObservableProperty]
    public partial string GoalLine { get; set; }

    [ObservableProperty]
    public partial MaterialIconKind GoalIcon { get; set; } = MaterialIconKind.TrackChanges;

    [ObservableProperty]
    public partial IBrush GoalIconBrush { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<GoalReviewFact> Facts { get; set; } = [];

    /// <summary>Why an answer didn't go through ("Deep work can't be paused"); null for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string? Message { get; set; }

    public bool HasMessage => Message is not null;

    /// <summary>Every goal has an answer: "Reviewed 8 goals" and Done.</summary>
    [ObservableProperty]
    public partial bool IsFinished { get; set; }

    [ObservableProperty]
    public partial string FinishedText { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>The highlighted decision (0 to 3).</summary>
    public int Lit => Decisions.ToList().FindIndex(decision => decision.IsLit);

    /// <summary>The goal shown now; null when none is asked about.</summary>
    public Goal? Shown => Index >= 0 && Index < _goals.Count ? _goals[Index] : null;

    /// <summary>The dialog closed (Stop for now, Esc, Done, a session): the answers given stay.</summary>
    public void Closed()
    {
        _closed = true;
        _review.Changed -= OnReviewChanged;
    }

    [RelayCommand]
    private void StopForNow() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Done() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => Index--;

    private bool CanGoBack() => Index > 0 && !IsFinished;

    /// <summary>→: the next goal without answering this one.</summary>
    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void Forward() => Index++;

    private bool CanGoForward() => Index < Total - 1 && !IsFinished;

    /// <summary>↑/↓: the highlight moves (it wraps, over the ones that can't apply).</summary>
    public void MoveHighlight(int by)
    {
        int lit = Math.Max(0, Lit);
        for (int step = 1; step <= Decisions.Count; step++)
        {
            int next = ((lit + (by * step)) % Decisions.Count + Decisions.Count) % Decisions.Count;
            if (Decisions[next].IsAvailable)
            {
                Highlight(next);
                return;
            }
        }
    }

    /// <summary>The pointer over a decision highlights it.</summary>
    public void Highlight(int index)
    {
        for (int i = 0; i < Decisions.Count; i++)
        {
            Decisions[i].IsLit = i == index;
        }

        OnPropertyChanged(nameof(Lit));
    }

    /// <summary>Enter: the highlighted decision.</summary>
    public Task ChooseLitAsync()
    {
        int lit = Lit;
        return lit < 0 ? Task.CompletedTask : ChooseAsync(Decisions[lit].Choice);
    }

    /// <summary>An answer for the goal shown: kept at once; Adjust opens Edit goal › Target; then the next unanswered goal (or the end).</summary>
    public async Task ChooseAsync(GoalReviewChoice choice)
    {
        if (IsBusy || IsFinished || _closed || Shown is not Goal goal || Decisions.First(decision => decision.Choice == choice) is { IsAvailable: false })
        {
            return;
        }

        IsBusy = true;
        try
        {
            GoalChangeResult result = await _review.DecideAsync(Week, goal.Id, choice);
            if (result is not (GoalChangeResult.Applied or GoalChangeResult.Unchanged))
            {
                Message = _review.SessionRunning ? "Not during a focus session." : "That didn't go through. Nothing was changed.";
                return;
            }

            Message = null;
            if (choice == GoalReviewChoice.Adjust && AdjustTarget is not null)
            {
                GoalReviewAdjustment? adjustment = GoalReviewAdjustment.For(goal, _review.SuggestionFor(goal), _today);
                await AdjustTarget(goal.Id, adjustment);
            }
        }
        finally
        {
            IsBusy = false;
        }

        if (_closed)
        {
            return;
        }

        if (FirstUnanswered(Index + 1) is int next)
        {
            if (next == Index)
            {
                // The one goal left unanswered is the one shown (an earlier one was skipped with →): show its answer now.
                Load();
            }
            else
            {
                Index = next;
            }

            return;
        }

        Finish();
    }

    partial void OnIndexChanged(int value) => Load();

    // Every goal answered: the tidy-up straight after when it is due (the app opens it once this closes), else "Reviewed N goals".
    private void Finish()
    {
        if (_review.TidyUpDue)
        {
            ThenTidyUp = true;
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        FinishedText = Total == 1 ? "Reviewed 1 goal" : $"Reviewed {Total} goals";
        IsFinished = true;
        BackCommand.NotifyCanExecuteChanged();
        ForwardCommand.NotifyCanExecuteChanged();
    }

    // The next goal without an answer in the week's record, from `from` on (wrapping); null when every goal has one.
    private int? FirstUnanswered(int from)
    {
        GoalReviewRecord? record = _review.RecordFor(Week);
        for (int step = 0; step < _goals.Count; step++)
        {
            int at = (from + step) % _goals.Count;
            if (record?.Decisions.Any(decision => decision.GoalId == _goals[at].Id) != true)
            {
                return at;
            }
        }

        return null;
    }

    // A session started: the review closes (its answers stay), as the tidy-up does.
    private void OnReviewChanged(object? sender, EventArgs e)
    {
        if (!_closed && _review.SessionRunning)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    // The goal at Index, its facts and the decisions (suggested, answered, available).
    private void Load()
    {
        string week = GoalReviewService.WeekText(Week);
        if (Shown is not Goal goal)
        {
            Sub = $"week of {week}";
            PositionText = string.Empty;
            return;
        }

        Sub = $"{Index + 1} of {Total} · week of {week} · ↑/↓ and Enter, or 1–4";
        PositionText = $"{Index + 1} / {Total}";
        GoalName = goal.Name;
        string? area = _review.Goals.Lists.Areas.FirstOrDefault(item => item.Id == goal.AreaId && item.IsLive)?.Name;
        string target = GoalTexts.TargetLine(goal, _today);
        GoalLine = area is null ? target : $"{target} · {area}";
        GoalIcon = GoalIcons.Of(goal.Icon);
        GoalIconBrush = GoalIcons.Brush(goal.Color);

        GoalReviewSuggestion suggestion = _review.SuggestionFor(goal);
        Facts = FactsOf(goal, suggestion);
        GoalReviewChoice? answered = _review.RecordFor(Week)?.Decisions.FirstOrDefault(decision => decision.GoalId == goal.Id)?.Choice;
        bool canPause = GoalLowering.CanPauseOrDelete(goal);
        bool waits = goal.IsDeepWork && !_review.IsProductaBuddy;
        foreach (GoalReviewDecisionViewModel decision in Decisions)
        {
            decision.IsAvailable = decision.Choice != GoalReviewChoice.Pause || canPause;
            decision.Sub = decision.Choice switch
            {
                GoalReviewChoice.KeepGoing => "Same target · no change",
                GoalReviewChoice.Adjust => waits
                    ? $"Move the date or the amount · lowering waits {GoalTexts.Minutes(_review.Goals.LooserWait.TotalMinutes)}"
                    : "Move the date or the amount",
                GoalReviewChoice.Pause => canPause ? "Stops counting and reminders · keeps history" : "Deep work can't be paused",
                _ => "Skips this goal in the review",
            };
            decision.IsSuggested = decision.Choice == suggestion.Choice && decision.IsAvailable && answered is null;
            decision.IsChosen = decision.Choice == answered;
        }

        GoalReviewChoice lit = answered ?? (Decisions.First(decision => decision.Choice == suggestion.Choice).IsAvailable ? suggestion.Choice : GoalReviewChoice.KeepGoing);
        Highlight(Decisions.ToList().FindIndex(decision => decision.Choice == lit));
        Message = null;
        BackCommand.NotifyCanExecuteChanged();
        ForwardCommand.NotifyCanExecuteChanged();
    }

    // The facts the suggestion used (brief §5.8): behind (amber), the 30-day change, the pace, "At this pace: <date>".
    private List<GoalReviewFact> FactsOf(Goal goal, GoalReviewSuggestion suggestion)
    {
        var facts = new List<GoalReviewFact>();
        if (suggestion.Quiet)
        {
            facts.Add(new GoalReviewFact("Nothing for 3 weeks", IsAmber: true));
        }

        if (suggestion.BehindDays > 0)
        {
            facts.Add(new GoalReviewFact(suggestion.BehindDays == 1 ? "1 day behind" : $"{suggestion.BehindDays} days behind", IsAmber: true));
        }

        if (suggestion.BehindPeriods >= 2)
        {
            facts.Add(new GoalReviewFact($"Behind in {suggestion.BehindPeriods} of the last {suggestion.PeriodsChecked}", IsAmber: true));
        }

        GoalUnit unit = goal.Target.Unit;
        if (goal.Kind == GoalKind.Amount && goal.Target.Direction == GoalDirection.Reach && ReadingChange(goal) is (double latest, double change))
        {
            facts.Add(new GoalReviewFact($"{GoalTexts.Value(latest, unit)} · {Signed(change, unit)} in 30 days"));
        }
        else if (suggestion.ChangePercent30 is double percent)
        {
            facts.Add(new GoalReviewFact($"{(percent >= 0 ? "+" : "−")}{Math.Abs(Math.Round(percent)).ToString(CultureInfo.InvariantCulture)}% in 30 days"));
        }

        if (suggestion.PacePerDay is double pace && pace > 0)
        {
            facts.Add(new GoalReviewFact(unit == GoalUnit.Minutes
                ? $"Pace: {GoalTexts.Minutes(pace)} a day"
                : $"Pace: {GoalTexts.Value(pace * 7, unit)} a week"));
        }

        if (suggestion.ProjectedFinish is DateOnly finish)
        {
            facts.Add(new GoalReviewFact($"At this pace: {finish.ToString("MMM d", CultureInfo.InvariantCulture)}"));
        }

        if (facts.Count == 0)
        {
            facts.Add(new GoalReviewFact("Not much history yet"));
        }

        return facts;
    }

    // A level goal's latest reading and its change over the last 30 days; null without two readings.
    private (double Latest, double Change)? ReadingChange(Goal goal)
    {
        IReadOnlyList<GoalDayState> days = GoalCounting.Days(goal, _review.Data.Current, _today.AddDays(-30), _today, _today);
        List<GoalDayState> readings = days.Where(state => state.HasValue).ToList();
        return readings.Count < 2 ? null : (readings[^1].Value, readings[^1].Value - readings[0].Value);
    }

    private static string Signed(double change, GoalUnit unit)
    {
        string magnitude = GoalTexts.Value(Math.Abs(change), unit);
        return change < 0 ? "−" + magnitude : "+" + magnitude;
    }
}
