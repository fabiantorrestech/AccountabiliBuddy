using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// A task row of the checklist with its subtasks: tick it, pick it, add a subtask, put it in a category, park it as a
/// come-back, or rename, move or remove it.
/// </summary>
public sealed partial class TaskRowViewModel : ChecklistRowViewModel
{
    /// <param name="shown">What the on-screen bar shows, to mark this task or one of its subtasks with ▸.</param>
    /// <param name="categories">The task categories, for its colour dot and its category menu.</param>
    public TaskRowViewModel(SessionTask task, string? doneText, ShownTask? shown, IReadOnlyList<TaskCategory> categories, SessionTasksService tasks)
        : base(task.Id, task.Title, task.IsDone, shown is { Subtask: null } && shown.Task.Id == task.Id, tasks)
    {
        DoneText = doneText;
        NewSubtaskTitle = string.Empty;
        foreach (SessionSubtask subtask in task.Subtasks)
        {
            Subtasks.Add(new SubtaskRowViewModel(subtask, shown?.Subtask?.Id == subtask.Id, tasks));
        }

        TaskCategory? category = categories.FirstOrDefault(item => item.Id == task.CategoryId);
        CategoryId = category?.Id;
        CategoryName = category?.Name ?? "No category";
        CategoryBrush = TaskCategoryBrushes.Of(category?.Color);
        CategoryChoices =
        [
            .. categories.Select(item => new TaskCategoryChoiceViewModel(
                item.Id, item.Name, TaskCategoryBrushes.Of(item.Color), item.Id == CategoryId, SetCategoryAsync)),
            new TaskCategoryChoiceViewModel(null, "No category", Brushes.Transparent, CategoryId is null, SetCategoryAsync),
        ];
    }

    /// <summary>E.g. "Done 10:42 AM · 25m" for a ticked task; null while open.</summary>
    public string? DoneText { get; }

    public ObservableCollection<SubtaskRowViewModel> Subtasks { get; } = new();

    public bool HasSubtasks => Subtasks.Count > 0;

    /// <summary>Its category; null = no category.</summary>
    public Guid? CategoryId { get; }

    public bool HasCategory => CategoryId is not null;

    /// <summary>E.g. "Work", or "No category".</summary>
    public string CategoryName { get; }

    /// <summary>Its category's colour (its dot); transparent with no category.</summary>
    public IBrush CategoryBrush { get; }

    /// <summary>The menu behind its dot: every category, then "No category".</summary>
    public IReadOnlyList<TaskCategoryChoiceViewModel> CategoryChoices { get; }

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

    /// <summary>⚑: it leaves the checklist as a come-back (with " (+N steps)" for its open subtasks).</summary>
    [RelayCommand]
    private Task ParkAsync()
    {
        return Tasks.ParkAsync(Id);
    }

    private Task SetCategoryAsync(Guid? categoryId)
    {
        return categoryId == CategoryId ? Task.CompletedTask : Tasks.SetCategoryAsync(Id, categoryId);
    }
}
