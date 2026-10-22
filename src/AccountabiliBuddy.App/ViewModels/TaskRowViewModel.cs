using System.Collections.ObjectModel;
using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A task row of the checklist with its subtasks: tick it, add a subtask, or remove it.</summary>
public sealed partial class TaskRowViewModel : ObservableObject
{
    private readonly Func<Guid, Task> _toggle;
    private readonly Func<Guid, Task> _remove;
    private readonly Func<string, Guid, Task> _addSubtask;

    public TaskRowViewModel(
        SessionTask task, string? doneText, Func<Guid, Task> toggle, Func<Guid, Task> remove, Func<string, Guid, Task> addSubtask)
    {
        Id = task.Id;
        Title = task.Title;
        DoneText = doneText;
        _isDone = task.IsDone;
        _toggle = toggle;
        _remove = remove;
        _addSubtask = addSubtask;
        NewSubtaskTitle = string.Empty;
        foreach (SessionSubtask subtask in task.Subtasks)
        {
            Subtasks.Add(new SubtaskRowViewModel(subtask, toggle, remove));
        }
    }

    public Guid Id { get; }

    public string Title { get; }

    /// <summary>E.g. "Done 10:42 AM · 25m" for a ticked task; null while open.</summary>
    public string? DoneText { get; }

    public ObservableCollection<SubtaskRowViewModel> Subtasks { get; } = new();

    public bool HasSubtasks => Subtasks.Count > 0;

    private bool _isDone;

    /// <summary>Ticking it here ticks it (and its subtasks) in the checklist.</summary>
    public bool IsDone
    {
        get => _isDone;
        set
        {
            if (value != _isDone)
            {
                _isDone = value;
                _ = _toggle(Id);
            }
        }
    }

    [ObservableProperty]
    public partial bool IsAddingSubtask { get; set; }

    [ObservableProperty]
    public partial string NewSubtaskTitle { get; set; }

    [RelayCommand]
    private void BeginAddSubtask()
    {
        IsAddingSubtask = !IsAddingSubtask;
    }

    [RelayCommand]
    private async Task AddSubtaskAsync()
    {
        string title = NewSubtaskTitle;
        NewSubtaskTitle = string.Empty;
        await _addSubtask(title, Id);
    }

    [RelayCommand]
    private Task RemoveAsync()
    {
        return _remove(Id);
    }
}
