using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A task row of the checklist with its subtasks: tick it, pick it, add a subtask, or rename, move or remove it.</summary>
public sealed partial class TaskRowViewModel : ChecklistRowViewModel
{
    /// <param name="shown">What the on-screen bar shows, to mark this task or one of its subtasks with ▸.</param>
    public TaskRowViewModel(SessionTask task, string? doneText, ShownTask? shown, SessionTasksService tasks)
        : base(task.Id, task.Title, task.IsDone, shown is { Subtask: null } && shown.Task.Id == task.Id, tasks)
    {
        DoneText = doneText;
        NewSubtaskTitle = string.Empty;
        foreach (SessionSubtask subtask in task.Subtasks)
        {
            Subtasks.Add(new SubtaskRowViewModel(subtask, shown?.Subtask?.Id == subtask.Id, tasks));
        }
    }

    /// <summary>E.g. "Done 10:42 AM · 25m" for a ticked task; null while open.</summary>
    public string? DoneText { get; }

    public ObservableCollection<SubtaskRowViewModel> Subtasks { get; } = new();

    public bool HasSubtasks => Subtasks.Count > 0;

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
        await Tasks.AddAsync(title, Id);
    }
}
