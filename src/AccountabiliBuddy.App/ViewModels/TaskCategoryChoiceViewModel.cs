using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One entry of a checklist category menu: a category (or "No category") to put a task, or new tasks, in.</summary>
public sealed partial class TaskCategoryChoiceViewModel
{
    private readonly Func<Guid?, Task> _pick;

    /// <param name="id">The category; null = no category.</param>
    /// <param name="isSelected">The one the task (or the "Add a task" box) has now, marked ✓.</param>
    public TaskCategoryChoiceViewModel(Guid? id, string name, IBrush brush, bool isSelected, Func<Guid?, Task> pick)
    {
        Id = id;
        Name = name;
        Brush = brush;
        IsSelected = isSelected;
        _pick = pick;
    }

    public Guid? Id { get; }

    public string Name { get; }

    public IBrush Brush { get; }

    public bool IsSelected { get; }

    [RelayCommand]
    private Task PickAsync()
    {
        return _pick(Id);
    }
}
