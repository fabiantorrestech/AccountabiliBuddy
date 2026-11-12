using System.Collections.ObjectModel;
using System.Globalization;
using AccountabiliBuddy.Core.Domain;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The open task (spec 03 §B, 05 §3), in the Tasks page's right card: its kind line, the title (edited in place on a click), the
/// chips row (label, tags and "+ Tag", time limit, reminders and due date, highlight), Steps · n/m with each step's letter chip,
/// focus tag, time and drag handle, the add-a-step box, the notes box (saved on blur or after a short pause), the reminders line,
/// the moves, and the footer (sync line, ⋯, the ID chip that copies "142 · title"). It reads its item again when a store changes,
/// but never overwrites a title or a note that is being typed. Notes are private text: nothing here logs them.
/// </summary>
public sealed partial class TaskDetailViewModel : ObservableObject, IDisposable, ITaskMenuSource
{
    private static readonly int[] StandardEstimates = [25, 50, 90];
    private static readonly TimeSpan NoteDelay = TimeSpan.FromMilliseconds(700);

    private readonly TasksViewModel _owner;
    private TaskSnapshot _snapshot;
    private CancellationTokenSource? _noteSave;

    public TaskDetailViewModel(TaskSnapshot snapshot, TasksViewModel owner)
    {
        _owner = owner;
        _snapshot = snapshot;
        Title = snapshot.Title;
        Note = snapshot.Note ?? string.Empty;
        NewStepText = CustomEstimateText = KindText = RunningText = PlanText = Message = string.Empty;
        NumberText = LabelName = TimeLimitText = ReminderText = StepsHead = StepsSub = SyncText = string.Empty;
        LabelBrush = Brushes.Transparent;
        Apply(snapshot, first: true);
    }

    public TaskRowKey Key => _snapshot.Key;

    public TaskRowKind Kind => _snapshot.Key.Kind;

    /// <summary>The Muted line over the title, e.g. "In this session · 1 of 3".</summary>
    [ObservableProperty]
    public partial string KindText { get; set; }

    /// <summary>The title; Enter or leaving the box keeps it.</summary>
    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string Note { get; set; }

    [ObservableProperty]
    public partial string NewStepText { get; set; }

    public ObservableCollection<TaskStepViewModel> Steps { get; } = new();

    /// <summary>The estimate chips: 25, 50, 90 min, and the task's own value when it is none of them.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<TaskChipViewModel> EstimateChips { get; set; } = [];

    [ObservableProperty]
    public partial bool IsCustomEstimateOpen { get; set; }

    [ObservableProperty]
    public partial string CustomEstimateText { get; set; }

    /// <summary>The Later item's due date (the date picker's value); null for none.</summary>
    [ObservableProperty]
    public partial DateTime? DueDate { get; set; }

    /// <summary>E.g. "Planned for Fri 3:00 PM · Deep work"; empty when not in the plan.</summary>
    [ObservableProperty]
    public partial string PlanText { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<PlanDayChoiceViewModel> PlanDays { get; set; } = [];

    /// <summary>"Running · 18:42 left", the bar of the task the session is on.</summary>
    [ObservableProperty]
    public partial string RunningText { get; set; }

    /// <summary>Why a move was refused, e.g. "This session already has 3 tasks."</summary>
    [ObservableProperty]
    public partial string Message { get; set; }

    [ObservableProperty]
    public partial bool CanStart { get; set; }

    public bool IsDone => _snapshot.IsDone;

    // ---- The open task's chips row, steps head, reminders line and footer (from the task list's copy of the task, when it has one) ----

    private const string NoReminders = "Reminders & due date";

    /// <summary>The ID chip's number, "142"; empty while the task has none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNumber))]
    public partial string NumberText { get; set; }

    public bool HasNumber => NumberText.Length > 0;

    /// <summary>What Copy ID puts on the clipboard: "142 · title".</summary>
    public string CopyText => HasNumber ? $"{NumberText} · {_snapshot.Title}" : _snapshot.Title;

    [ObservableProperty]
    public partial string LabelName { get; set; }

    [ObservableProperty]
    public partial IBrush LabelBrush { get; set; }

    /// <summary>The label chip opens the label picker (every kind but Auto-saved).</summary>
    public bool CanLabel => Kind != TaskRowKind.AutoSaved;

    /// <summary>The task's tags as chips, "#errands".</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> TagChips { get; set; } = [];

    /// <summary>"+ Tag" (ADR 72's labels menu; the full Tags menu is 3.5); not on a task another PC's record holds.</summary>
    [ObservableProperty]
    public partial bool CanTag { get; set; }

    /// <summary>The time limit chip: "50 min", or "Time limit" for none.</summary>
    [ObservableProperty]
    public partial string TimeLimitText { get; set; }

    /// <summary>The reminders line: "Today 3:00 PM · 10 min before", "2 reminders · due Fri 11/13", or "Reminders & due date".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReminders))]
    public partial string ReminderText { get; set; }

    public bool HasReminders => ReminderText != NoReminders;

    /// <summary>The task is highlighted (star and tint).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HighlightIcon), nameof(HighlightTip))]
    public partial bool IsHighlighted { get; set; }

    /// <summary>The star can be changed: the task list holds the task.</summary>
    [ObservableProperty]
    public partial bool CanHighlight { get; set; }

    public MaterialIconKind HighlightIcon => IsHighlighted ? MaterialIconKind.Star : MaterialIconKind.StarOutline;

    public string HighlightTip => IsHighlighted ? "Remove highlight" : "Highlight";

    /// <summary>"Steps · 1/3"; "Steps" with none.</summary>
    [ObservableProperty]
    public partial string StepsHead { get; set; }

    /// <summary>"add up to 35 min of 50 min", "within 50 min", "no time limit".</summary>
    [ObservableProperty]
    public partial string StepsSub { get; set; }

    /// <summary>The footer's sync line: "Synced · Pixel 8, Home desktop", "Changed on both devices", "Kept on this PC only".</summary>
    [ObservableProperty]
    public partial string SyncText { get; set; }

    [ObservableProperty]
    public partial MaterialIconKind SyncIcon { get; set; } = MaterialIconKind.CloudCheckOutline;

    /// <summary>The sync line is a problem (red).</summary>
    [ObservableProperty]
    public partial bool IsSyncProblem { get; set; }

    /// <summary>The ⋯ in the footer: the row's own menu.</summary>
    public IReadOnlyList<TaskMenuEntry> BuildMenu() => _owner.MenuFor(Key);

    /// <summary>The label chip's picker (the same choices as ⋯ › Label).</summary>
    public IReadOnlyList<TaskMenuEntry> LabelMenu() => _owner.LabelMenuFor(Key);

    /// <summary>"+ Tag": the tags this device knows, ticked where the task has them, and "New tag…".</summary>
    public IReadOnlyList<TaskMenuEntry> TagMenu() => _owner.TagMenuFor(Key);

    /// <summary>The time limit chip's menu: 10, 15, 25, 50, 90 min and No limit (the chosen one ticked).</summary>
    public IReadOnlyList<TaskMenuEntry> TimeLimitMenu()
    {
        var entries = new List<TaskMenuEntry>();
        foreach (int minutes in new[] { 10, 15, 25, 50, 90 })
        {
            int chosen = minutes;
            entries.Add(new TaskMenuEntry(
                EstimateText.Of(minutes), new AsyncRelayCommand(() => _owner.SetEstimateAsync(Key, chosen)), IsChecked: _snapshot.EstimateMinutes == minutes,
                Icon: MaterialIconKind.TimerOutline));
        }

        entries.Add(TaskMenuEntry.Async("No limit", () => _owner.SetEstimateAsync(Key, null), MaterialIconKind.TimerOffOutline));
        entries.Add(TaskMenuEntry.Do("Custom…", OpenCustomEstimate, MaterialIconKind.PencilOutline));
        return entries;
    }

    [RelayCommand]
    private Task ToggleHighlightAsync() => _owner.ToggleHighlightAsync(Key);

    /// <summary>A step dropped by its handle (or Alt+↑/↓) before or after another step of this task.</summary>
    public Task MoveStepAsync(Guid stepId, Guid targetStepId, bool after) => _owner.MoveStepAsync(Key, stepId, targetStepId, after);

    /// <summary>Steps, estimate, notes and the title can be changed (not on a task that is already done).</summary>
    public bool CanEdit => !_snapshot.IsDone;

    public bool IsComeBack => Kind == TaskRowKind.ComeBack;

    public bool IsLater => Kind == TaskRowKind.Later;

    /// <summary>A come-back has only a title, its line and the moves; a done or past task is read-only except its title and new steps.</summary>
    public bool ShowsFields => Kind is TaskRowKind.Session or TaskRowKind.Later or TaskRowKind.Done or TaskRowKind.Past or TaskRowKind.Queued;

    /// <summary>The title can be renamed, also on a done or past task (from here or the ⋯ menu).</summary>
    public bool CanEditTitle => true;

    /// <summary>"Add a step" shows for every task but a come-back (a new step reopens a done task).</summary>
    public bool CanAddStep => ShowsFields;

    public bool ShowsEditableFields => ShowsFields && CanEdit;

    public bool ShowsRunningBar => _snapshot.IsRunningTask;

    /// <summary>"Start with this": no session runs, and the task is one a session could start with.</summary>
    public bool ShowsStart => Kind is TaskRowKind.Session or TaskRowKind.Later && !_owner.IsSessionLive && !_snapshot.IsDone;

    public bool ShowsMoveToLater => Kind is TaskRowKind.Session or TaskRowKind.ComeBack or TaskRowKind.Done or TaskRowKind.Past;

    /// <summary>"Add to Today": from Later, a come-back, or a done / unfinished task (it comes back open as a copy).</summary>
    public bool ShowsMoveToSession => Kind is TaskRowKind.Later or TaskRowKind.ComeBack or TaskRowKind.Done or TaskRowKind.Past;

    // Put in plan is for the next session's tasks and Later; a running session's task can't be planned for later.
    public bool ShowsPlan => (Kind == TaskRowKind.Later || Kind == TaskRowKind.Session && !_owner.IsSessionLive) && !_snapshot.IsDone;

    public bool ShowsDelete => true;

    public bool HasPlan => PlanText.Length > 0;

    public bool HasPlanDays => PlanDays.Count > 0;

    public bool HasMessage => Message.Length > 0;

    /// <summary>A come-back's own line, e.g. "Parked 11:02 AM".</summary>
    public string ParkedText => _snapshot.ParkedText ?? string.Empty;

    public bool HasParkedText => ParkedText.Length > 0;

    partial void OnPlanDaysChanged(IReadOnlyList<PlanDayChoiceViewModel> value) => OnPropertyChanged(nameof(HasPlanDays));

    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));

    partial void OnPlanTextChanged(string value) => OnPropertyChanged(nameof(HasPlan));

    partial void OnNoteChanged(string value)
    {
        if (!CanEdit || value == (_snapshot.Note ?? string.Empty))
        {
            return;
        }

        // A short pause after the last key keeps the note; leaving the box keeps it at once (CommitNote).
        _noteSave?.Cancel();
        _noteSave = new CancellationTokenSource();
        _ = SaveNoteAfterAsync(_noteSave.Token);
    }

    partial void OnDueDateChanged(DateTime? value)
    {
        DateOnly? due = value is DateTime date ? DateOnly.FromDateTime(date) : null;
        if (!IsLater || due == _snapshot.DueOn)
        {
            return;
        }

        _ = _owner.EditLaterAsync(Key.Id, item => item with { DueOn = due });
    }

    /// <summary>Reads the item again after a store changed; what is being typed is left alone.</summary>
    public void Reload(TaskSnapshot snapshot)
    {
        bool titleDirty = Title != _snapshot.Title;
        bool noteDirty = Note != (_snapshot.Note ?? string.Empty);
        _snapshot = snapshot;
        Apply(snapshot, first: false);
        if (!titleDirty)
        {
            Title = snapshot.Title;
        }

        if (!noteDirty)
        {
            Note = snapshot.Note ?? string.Empty;
        }
    }

    /// <summary>Keeps a title or note typed but not yet kept; called before another item is shown.</summary>
    public void Flush()
    {
        _noteSave?.Cancel();
        _ = CommitNoteAsync();
        _ = CommitTitleAsync();
    }

    public void Dispose()
    {
        _noteSave?.Cancel();
    }

    /// <summary>Why a move was refused, shown under the footer's buttons.</summary>
    public void SetMessage(string text)
    {
        Message = text;
    }

    /// <summary>The running bar's text, kept up to date by the page (the session's time left).</summary>
    public void SetRunningText(string label, string timeLeft)
    {
        RunningText = timeLeft.Length > 0 ? $"{label} · {timeLeft} left" : label;
    }

    [RelayCommand]
    private Task CommitTitleAsync()
    {
        string title = Title.Trim();
        if (title.Length == 0)
        {
            Title = _snapshot.Title;
            return Task.CompletedTask;
        }

        return title == _snapshot.Title ? Task.CompletedTask : _owner.RenameAsync(Key, title);
    }

    [RelayCommand]
    private async Task CommitNoteAsync()
    {
        _noteSave?.Cancel();
        string note = Note;
        if (CanEdit && note != (_snapshot.Note ?? string.Empty))
        {
            await _owner.SetNoteAsync(Key, note);
        }
    }

    private async Task SaveNoteAfterAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(NoteDelay, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await CommitNoteAsync();
    }

    [RelayCommand]
    private async Task AddStepAsync()
    {
        string title = NewStepText.Trim();
        NewStepText = string.Empty;
        if (title.Length > 0 && CanAddStep)
        {
            await _owner.AddStepAsync(Key, title);
        }
    }

    [RelayCommand]
    private void OpenCustomEstimate()
    {
        CustomEstimateText = _snapshot.EstimateMinutes?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        IsCustomEstimateOpen = true;
    }

    [RelayCommand]
    private async Task ApplyCustomEstimateAsync()
    {
        IsCustomEstimateOpen = false;
        if (int.TryParse(CustomEstimateText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int minutes) && minutes > 0)
        {
            await _owner.SetEstimateAsync(Key, minutes);
        }
    }

    [RelayCommand]
    private void CancelCustomEstimate()
    {
        IsCustomEstimateOpen = false;
    }

    [RelayCommand]
    private Task MoveToLaterAsync() => _owner.MoveToLaterAsync(Key);

    [RelayCommand]
    private Task AddToTodayAsync() => _owner.AddToTodayAsync(Key);

    [RelayCommand]
    private Task StartWithThisAsync() => _owner.StartWithThisAsync(Key);

    [RelayCommand]
    private Task MoveToComebackAsync() => _owner.MoveToComebackAsync(Key);

    [RelayCommand]
    private Task DeleteAsync() => _owner.DeleteAsync(Key);

    /// <summary>A come-back's check box in the detail card: done.</summary>
    [RelayCommand]
    private Task TickComeBackAsync() => _owner.TickComeBackAsync(Key.Id);

    [RelayCommand]
    private void ClearDue()
    {
        DueDate = null;
    }

    [RelayCommand]
    private Task ClearPlanAsync() => _owner.SetPlannedForAsync(Key, null);

    private void Apply(TaskSnapshot snapshot, bool first)
    {
        KindText = snapshot.KindText;
        Steps.Clear();
        IReadOnlySet<Guid> focused = snapshot.FocusedSteps ?? new HashSet<Guid>();
        string number = snapshot.Item?.Number is TaskNumber taskNumber ? taskNumber.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
        bool canFocus = _owner.CanFocusSteps(Key);
        bool canMove = CanEdit && _owner.CanMoveSteps(Key);
        for (int index = 0; index < snapshot.Steps.Count; index++)
        {
            SessionSubtask step = snapshot.Steps[index];
            string letter = TaskStepLetters.Of(index);
            int? minutes = snapshot.Item?.Steps?.FirstOrDefault(stored => stored.Id == step.Id)?.EstimateMinutes;
            bool isFocused = focused.Contains(step.Id) && !step.IsDone;
            Guid stepId = step.Id;
            Steps.Add(new TaskStepViewModel(
                step.Id, step.Title, step.IsDone, CanEdit, id => _owner.ToggleStepAsync(Key, id), id => _owner.RemoveStepAsync(Key, id),
                (id, title) => _owner.RenameStepAsync(Key, id, title), (id, title) => _owner.AddStepAfterAsync(Key, id, title))
            {
                Letter = letter,
                LetterTip = number.Length > 0 ? number + letter : "Step " + letter,
                IsFocused = isFocused,
                TimeText = minutes is int stepMinutes ? EstimateText.Of(stepMinutes) : string.Empty,
                CanDrag = canMove,
                FocusCommand = canFocus && !step.IsDone && !isFocused ? new AsyncRelayCommand(() => _owner.FocusStepAsync(stepId)) : null,
            });
        }

        ApplyOpenTask(snapshot, number);

        EstimateChips = BuildEstimateChips(snapshot.EstimateMinutes);
        PlanText = _owner.PlanTextOf(snapshot.PlannedFor);
        PlanDays = _owner.BuildPlanDays(snapshot.PlannedFor, slot => _owner.SetPlannedForAsync(Key, slot));
        DueDate = snapshot.DueOn is DateOnly due ? due.ToDateTime(TimeOnly.MinValue) : null;
        CanStart = _owner.CanStartSession;
        if (!first)
        {
            OnPropertyChanged(nameof(IsDone));
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(ShowsEditableFields));
            OnPropertyChanged(nameof(ShowsRunningBar));
            OnPropertyChanged(nameof(ShowsStart));
            OnPropertyChanged(nameof(ShowsMoveToLater));
            OnPropertyChanged(nameof(ShowsPlan));
            OnPropertyChanged(nameof(ParkedText));
            OnPropertyChanged(nameof(HasParkedText));
            OnPropertyChanged(nameof(CopyText));
        }
    }

    // The chips row, the steps head, the reminders line and the sync line.
    private void ApplyOpenTask(TaskSnapshot snapshot, string number)
    {
        TaskItem? item = snapshot.Item;
        NumberText = number;
        (string? labelName, string? labelColor) = _owner.LabelOf(Key, item);
        LabelName = labelName ?? "No label";
        LabelBrush = labelName is null ? Brushes.Transparent : TaskCategoryBrushes.Of(labelColor);
        TagChips = (item?.Tags ?? _owner.TagsOf(Key) ?? []).Select(tag => "#" + tag).ToList();
        CanTag = _owner.CanTag(Key);
        TimeLimitText = snapshot.EstimateMinutes is int minutes ? EstimateText.Of(minutes) : "Time limit";
        ReminderText = _owner.ReminderLineOf(item) ?? NoReminders;
        IsHighlighted = item?.Highlight == true;
        CanHighlight = item is not null;

        int done = snapshot.Steps.Count(step => step.IsDone);
        StepsHead = snapshot.Steps.Count > 0 ? $"Steps · {done}/{snapshot.Steps.Count}" : "Steps";
        int stepMinutes = item?.Steps?.Sum(step => step.EstimateMinutes ?? 0) ?? 0;
        StepsSub = snapshot.EstimateMinutes is int limit
            ? stepMinutes > 0 ? $"add up to {Math.Max(0, limit - stepMinutes)} min of {limit} min" : $"within {limit} min"
            : "no time limit";

        (string syncText, MaterialIconKind syncIcon, bool problem) = _owner.SyncLineOf(item);
        SyncText = syncText;
        SyncIcon = syncIcon;
        IsSyncProblem = problem;
    }

    private List<TaskChipViewModel> BuildEstimateChips(int? current)
    {
        var chips = new List<TaskChipViewModel>();
        foreach (int minutes in StandardEstimates)
        {
            chips.Add(Chip(minutes, current));
        }

        if (current is int own && !StandardEstimates.Contains(own))
        {
            chips.Add(Chip(own, current));
        }

        return chips;
    }

    // Choosing the chip that is selected clears the estimate.
    private TaskChipViewModel Chip(int minutes, int? current)
    {
        bool selected = current == minutes;
        return new TaskChipViewModel(EstimateText.Of(minutes), selected, () => _owner.SetEstimateAsync(Key, selected ? null : minutes));
    }
}
