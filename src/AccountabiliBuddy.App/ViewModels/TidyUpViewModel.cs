using System.Globalization;
using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;
using AccountabiliBuddy.Core.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The weekly tidy-up flow (spec 05 §9, from the Shelf's tidy-up card): "Weekly tidy-up · 1 of 5 · untouched 3+ weeks", a card with the
/// task's large title and ← 1 / 5 → (back, skip without deciding), its fact chips, first three steps (+N more), note in italics and
/// origin; then the numbered decisions Keep / Move to Next / Archive / Delete with the highlighted ❯ row (the suggestion at first, or the
/// choice made before), "Suggested" and "Your choice" tags. Keys: K N A D or 1 to 4 decide, ↑/↓ move the highlight, Enter takes it, ←/→
/// step. A decision applies at once through <see cref="TaskTidyUpService"/> (changing it later re-applies from the task as it was) and
/// moves on; after the last one it shows "Tidied N of M tasks". "Stop for now" (or Esc) keeps what was decided. A session that starts
/// closes it (never during a session). Titles and notes are the user's text: shown only.
/// </summary>
public sealed partial class TidyUpViewModel : ObservableObject
{
    private const int StepsShown = 3;

    private readonly TaskTidyUpService _tidyUp;
    private readonly TaskStore _store;
    private readonly TimeProvider _time;
    private bool _closing;

    public TidyUpViewModel(TaskTidyUpService tidyUp, TaskStore store, TimeProvider time)
    {
        _tidyUp = tidyUp;
        _store = store;
        _time = time;
        Sub = PositionText = TaskTitle = Note = Origin = MoreSteps = FinishedText = string.Empty;
        Decisions =
        [
            new(TidyUpChoice.Keep, 1, "Keep", "Still matters. Resets its age", MaterialIconKind.CheckCircleOutline, new AsyncRelayCommand(() => ChooseAsync(TidyUpChoice.Keep))),
            new(TidyUpChoice.MoveToNext, 2, "Move to Next", "Do it soon", MaterialIconKind.ArrowRightCircleOutline, new AsyncRelayCommand(() => ChooseAsync(TidyUpChoice.MoveToNext))),
            new(TidyUpChoice.Archive, 3, "Archive", "Out of the way. It stays in History", MaterialIconKind.ArchiveOutline, new AsyncRelayCommand(() => ChooseAsync(TidyUpChoice.Archive))),
            new(TidyUpChoice.Delete, 4, "Delete", "Gone for good after 30 days in History", MaterialIconKind.DeleteOutline, new AsyncRelayCommand(() => ChooseAsync(TidyUpChoice.Delete))),
        ];
        _tidyUp.Changed += OnTidyUpChanged;
        Load();
    }

    /// <summary>Asks the dialog to close.</summary>
    public event EventHandler? CloseRequested;

    public IReadOnlyList<TidyUpDecisionViewModel> Decisions { get; }

    public string Keys => "K · N · A · D · Esc";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BackCommand), nameof(ForwardCommand))]
    public partial int Index { get; set; }

    public int Total => _tidyUp.Plan?.Total ?? 0;

    /// <summary>"1 of 5 · untouched 3+ weeks".</summary>
    [ObservableProperty]
    public partial string Sub { get; set; }

    /// <summary>"1 / 5" by the title.</summary>
    [ObservableProperty]
    public partial string PositionText { get; set; }

    [ObservableProperty]
    public partial string TaskTitle { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<TidyUpFact> Facts { get; set; } = [];

    /// <summary>The first three steps, "a. Draft the outline".</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> Steps { get; set; } = [];

    /// <summary>"+2 more"; empty for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMoreSteps))]
    public partial string MoreSteps { get; set; }

    public bool HasMoreSteps => MoreSteps.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    public partial string Note { get; set; }

    public bool HasNote => Note.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOrigin))]
    public partial string Origin { get; set; }

    public bool HasOrigin => Origin.Length > 0;

    /// <summary>The task went away another way (another device deleted or finished it): its decisions are off; → skips it.</summary>
    [ObservableProperty]
    public partial bool IsGone { get; set; }

    /// <summary>The last task was decided: "Tidied 4 of 5 tasks" and Done.</summary>
    [ObservableProperty]
    public partial bool IsFinished { get; set; }

    [ObservableProperty]
    public partial string FinishedText { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>The highlighted decision (0 to 3).</summary>
    public int Lit => Decisions.ToList().FindIndex(decision => decision.IsLit);

    /// <summary>The dialog closed (Stop for now, Esc, Done): what was decided stays; the card stays for the week unless it was finished.</summary>
    public void Closed()
    {
        _closing = true;
        _tidyUp.Changed -= OnTidyUpChanged;
        if (!IsFinished && _tidyUp.Plan is not null)
        {
            _tidyUp.Stop();
        }
    }

    [RelayCommand]
    private void StopForNow() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Done() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => Index--;

    private bool CanGoBack() => Index > 0 && !IsFinished;

    /// <summary>→: the next task without deciding this one.</summary>
    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void Forward() => Index++;

    private bool CanGoForward() => Index < Total - 1 && !IsFinished;

    /// <summary>↑/↓: the highlight moves (it wraps).</summary>
    public void MoveHighlight(int by)
    {
        int lit = Math.Max(0, Lit);
        Highlight((lit + by + Decisions.Count) % Decisions.Count);
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

    public async Task ChooseAsync(TidyUpChoice choice)
    {
        if (IsBusy || IsFinished || IsGone || _tidyUp.Plan is not TidyUpPlan)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _tidyUp.DecideAsync(Index, choice);
        }
        finally
        {
            IsBusy = false;
        }

        if (_tidyUp.Plan is not TidyUpPlan plan)
        {
            return;
        }

        if (Index < plan.Total - 1)
        {
            Index++;
            return;
        }

        // Finished first: the plan going away next is not a session starting.
        FinishedText = TaskToasts.Tidied(plan.Decided, plan.Total);
        IsFinished = true;
        BackCommand.NotifyCanExecuteChanged();
        ForwardCommand.NotifyCanExecuteChanged();
        await _tidyUp.FinishAsync();
    }

    partial void OnIndexChanged(int value) => Load();

    // The plan went away while the dialog was open (a session started): it closes.
    private void OnTidyUpChanged(object? sender, EventArgs e)
    {
        if (!_closing && !IsFinished && _tidyUp.Plan is null)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    // The task at Index as it was when the tidy-up began (its age and bin are what the decision is about), and its decisions.
    private void Load()
    {
        if (_tidyUp.Plan is not TidyUpPlan plan || plan.Total == 0)
        {
            return;
        }

        int index = Math.Clamp(Index, 0, plan.Total - 1);
        TidyUpItem item = plan.Items[index];
        Sub = plan.Sub(index);
        PositionText = $"{index + 1} / {plan.Total}";
        SyncTaskItem? live = _store.Lists.Tasks.FirstOrDefault(candidate => candidate.Id == item.TaskId && candidate.Task.IsLive);
        TaskItem? task = item.Original.Tasks.TryGetValue(item.TaskId, out SyncTaskItem? before) && before is not null ? before.Task : live?.Task;
        IsGone = item.Chosen is null && live is null;
        TaskTitle = task?.Title ?? "This task is gone";
        Facts = task is null ? [] : FactsOf(task);
        List<TaskStep> steps = (task?.Steps ?? []).ToList();
        Steps = steps.Take(StepsShown).Select((step, at) => TaskStepLetters.Of(at) + ". " + step.Title).ToList();
        MoreSteps = steps.Count > StepsShown ? $"+{steps.Count - StepsShown} more" : string.Empty;
        Note = task?.Note?.Trim() ?? string.Empty;
        Origin = task?.Origin?.Trim() ?? string.Empty;

        TidyUpChoice shown = item.Chosen ?? item.Suggested;
        for (int i = 0; i < Decisions.Count; i++)
        {
            TidyUpDecisionViewModel decision = Decisions[i];
            decision.IsSuggested = decision.Choice == item.Suggested && item.Chosen is null;
            decision.IsChosen = decision.Choice == item.Chosen;
            decision.IsLit = decision.Choice == shown;
        }

        OnPropertyChanged(nameof(Lit));
        BackCommand.NotifyCanExecuteChanged();
        ForwardCommand.NotifyCanExecuteChanged();
    }

    // ● label (or No label), bin, Untouched N wk, #tags, time limit, due date, reminders, Effort not set.
    private List<TidyUpFact> FactsOf(TaskItem task)
    {
        var facts = new List<TidyUpFact>();
        TaskLabel? label = task.LabelId is Guid id ? _store.Lists.Labels.Select(item => item.Label).FirstOrDefault(item => item.IsLive && item.Id == id) : null;
        facts.Add(label is null
            ? new TidyUpFact("No label", IsFaint: true)
            : new TidyUpFact(label.Name, TaskCategoryBrushes.Of(TaskLabelColors.Opaque(label.Color))));
        facts.Add(new TidyUpFact(TaskBins.Name(task.Bin), Icon: MaterialIconKind.TrayFull));
        facts.Add(new TidyUpFact($"Untouched {TaskUntouched.Weeks(task, _time.GetUtcNow())} wk", Icon: MaterialIconKind.Sleep));
        facts.AddRange((task.Tags ?? []).Select(tag => new TidyUpFact("#" + tag)));
        if (task.EstimateMinutes is int minutes)
        {
            facts.Add(new TidyUpFact(EstimateText.Of(minutes), Icon: MaterialIconKind.TimerOutline));
        }

        if (task.DueOn is DateOnly due)
        {
            string at = task.DueAt is TimeOnly time ? " " + time.ToString("h:mm tt", CultureInfo.InvariantCulture) : string.Empty;
            facts.Add(new TidyUpFact("Due " + due.ToString("ddd M/d", CultureInfo.InvariantCulture) + at, Icon: MaterialIconKind.CalendarBlankOutline));
        }

        if (task.Reminders is { Count: > 0 } reminders)
        {
            facts.Add(new TidyUpFact(reminders.Count == 1 ? "1 reminder" : reminders.Count + " reminders", Icon: MaterialIconKind.BellOutline));
        }

        facts.Add(new TidyUpFact("Effort not set", IsFaint: true));
        return facts;
    }
}
