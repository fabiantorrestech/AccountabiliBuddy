using System.Collections.ObjectModel;
using System.Globalization;
using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The Tasks page's right card for the selected task, Later item or come-back: its kind line, the title (edited in place),
/// steps, the estimate chips (25 / 50 / 90 min and a custom value), the notes box (saved on blur or after a short pause)
/// and the footer. It reads its item again when a store changes, but never overwrites a title or a note that is being typed.
/// Notes are private text: nothing here logs them.
/// </summary>
public sealed partial class TaskDetailViewModel : ObservableObject, IDisposable
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

    /// <summary>Steps, estimate, notes and the title can be changed (not on a task that is already done).</summary>
    public bool CanEdit => !_snapshot.IsDone;

    public bool IsComeBack => Kind == TaskRowKind.ComeBack;

    public bool IsLater => Kind == TaskRowKind.Later;

    /// <summary>A come-back has only a title, its line and the moves; a done task is read-only.</summary>
    public bool ShowsFields => Kind is TaskRowKind.Session or TaskRowKind.Later or TaskRowKind.Done;

    public bool ShowsEditableFields => ShowsFields && CanEdit;

    public bool ShowsRunningBar => _snapshot.IsRunningTask;

    /// <summary>"Start with this": no session runs, and the task is one a session could start with.</summary>
    public bool ShowsStart => Kind is TaskRowKind.Session or TaskRowKind.Later && !_owner.IsSessionLive && !_snapshot.IsDone;

    public bool ShowsMoveToLater => Kind == TaskRowKind.Session && !_snapshot.IsDone || Kind == TaskRowKind.ComeBack;

    public bool ShowsMoveToSession => Kind is TaskRowKind.Later or TaskRowKind.ComeBack;

    // Put in plan is for the next session's tasks and Later; a running session's task can't be planned for later.
    public bool ShowsPlan => (Kind == TaskRowKind.Later || Kind == TaskRowKind.Session && !_owner.IsSessionLive) && !_snapshot.IsDone;

    public bool ShowsDelete => Kind is TaskRowKind.Later or TaskRowKind.ComeBack;

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

        return title == _snapshot.Title || !CanEdit && !IsComeBack ? Task.CompletedTask : _owner.RenameAsync(Key, title);
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
        if (title.Length > 0 && CanEdit)
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
    private Task MoveToSessionAsync() => _owner.MoveToSessionAsync(Key);

    [RelayCommand]
    private Task StartWithThisAsync() => _owner.StartWithThisAsync(Key);

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
        foreach (SessionSubtask step in snapshot.Steps)
        {
            Steps.Add(new TaskStepViewModel(step.Id, step.Title, step.IsDone, CanEdit, id => _owner.ToggleStepAsync(Key, id), id => _owner.RemoveStepAsync(Key, id)));
        }

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
        }
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
