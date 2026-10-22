using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// A guided session's checklist (ADR 24, 25) as the quick-session popup, the Session tab and the on-screen window show it:
/// open tasks with their subtasks, the done ones under a collapsed "Done (n)", and "End session · all tasks done" when the
/// session may end at once. All views of it share <see cref="SessionTasksService"/>.
/// </summary>
public sealed partial class ChecklistViewModel : ObservableObject, IDisposable
{
    private readonly SessionTasksService _tasks;
    private readonly TimeProvider _time;

    public ChecklistViewModel(SessionTasksService tasks, ChecklistMode mode, TimeProvider time)
    {
        _tasks = tasks;
        _time = time;
        Mode = mode;
        NewTaskTitle = Heading = DoneHeader = ProgressText = CurrentTaskText = CurrentSubtaskText = string.Empty;
        _tasks.Changed += OnChanged;
        Refresh();
    }

    public ChecklistMode Mode { get; }

    public bool IsDraft => Mode == ChecklistMode.Draft;

    public bool IsCompact => Mode == ChecklistMode.Compact;

    /// <summary>Ticks show once a session runs.</summary>
    public bool CanTick => Mode != ChecklistMode.Draft;

    /// <summary>Adding, renaming, moving, removing and subtasks: not in the on-screen window (it can't take typing).</summary>
    public bool CanEdit => Mode != ChecklistMode.Compact;

    public ObservableCollection<TaskRowViewModel> OpenTasks { get; } = new();

    public ObservableCollection<TaskRowViewModel> DoneTasks { get; } = new();

    [ObservableProperty]
    public partial string Heading { get; set; }

    /// <summary>E.g. "Done (2)".</summary>
    [ObservableProperty]
    public partial string DoneHeader { get; set; }

    [ObservableProperty]
    public partial bool HasDone { get; set; }

    [ObservableProperty]
    public partial bool HasTasks { get; set; }

    /// <summary>E.g. "2/5".</summary>
    [ObservableProperty]
    public partial string ProgressText { get; set; }

    /// <summary>The task the on-screen window's bar shows (the picked one, else the first open task), e.g. "Fix tests".</summary>
    [ObservableProperty]
    public partial string CurrentTaskText { get; set; }

    /// <summary>Its subtask shown under it (the picked one, else its first open one); empty when it has none.</summary>
    [ObservableProperty]
    public partial string CurrentSubtaskText { get; set; }

    [ObservableProperty]
    public partial bool CanEndAtOnce { get; set; }

    [ObservableProperty]
    public partial string NewTaskTitle { get; set; }

    public bool NotifyAllDone
    {
        get => _tasks.NotifyAllDone;
        set
        {
            _ = _tasks.SetNotifyAllDoneAsync(value);
            OnPropertyChanged();
        }
    }

    /// <summary>The "Tell me when all are done" choice: on the Session tab while a session runs.</summary>
    public bool ShowsNotifyChoice => Mode == ChecklistMode.Live;

    /// <summary>Called by the views every few seconds so "End session" appears once the 10 minutes have passed.</summary>
    public void RefreshEndAtOnce()
    {
        CanEndAtOnce = Mode != ChecklistMode.Draft && _tasks.CanEndAtOnce;
    }

    public void Dispose()
    {
        _tasks.Changed -= OnChanged;
    }

    [RelayCommand]
    private async Task AddTaskAsync()
    {
        string title = NewTaskTitle;
        NewTaskTitle = string.Empty;
        await _tasks.AddAsync(title, null);
    }

    [RelayCommand]
    private void EndAtOnce()
    {
        _tasks.EndAtOnce();
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        Refresh();
    }

    private void Refresh()
    {
        SessionChecklist checklist = _tasks.Current;
        OpenTasks.Clear();
        DoneTasks.Clear();
        foreach (SessionTask task in checklist.Tasks)
        {
            var row = new TaskRowViewModel(task, DoneText(task, checklist), checklist.Shown, _tasks);
            (task.IsDone && CanTick ? DoneTasks : OpenTasks).Add(row);
        }

        HasTasks = checklist.HasTasks;
        HasDone = DoneTasks.Count > 0;
        DoneHeader = $"Done ({DoneTasks.Count})";
        ProgressText = checklist.HasTasks ? $"{checklist.DoneCount}/{checklist.Tasks.Count}" : string.Empty;
        CurrentTaskText = checklist.Shown?.Task.Title ?? (checklist.AllDone ? "All tasks done" : string.Empty);
        CurrentSubtaskText = checklist.Shown?.Subtask?.Title ?? string.Empty;
        Heading = Mode == ChecklistMode.Draft
            ? "Tasks for this session (optional)"
            : checklist.HasTasks ? $"Tasks · {ProgressText}" : "Tasks";
        RefreshEndAtOnce();
    }

    // "Done 10:42 AM · 25m": when, and how long since the previous tick (or the session's start).
    private string? DoneText(SessionTask task, SessionChecklist checklist)
    {
        if (task.DoneAt is not DateTimeOffset doneAt)
        {
            return null;
        }

        DateTimeOffset? before = checklist.Tasks
            .Where(other => other.DoneAt is DateTimeOffset at && at < doneAt)
            .Select(other => other.DoneAt)
            .Max() ?? checklist.SessionStart;
        string when = TimeZoneInfo.ConvertTime(doneAt, _time.LocalTimeZone).ToString("t");
        if (before is not DateTimeOffset from)
        {
            return $"Done {when}";
        }

        int minutes = (int)Math.Round((doneAt - from).TotalMinutes);
        return $"Done {when} · {(minutes >= 60 ? $"{minutes / 60}h {minutes % 60:00}m" : $"{minutes}m")}";
    }
}
