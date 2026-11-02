using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The Categories editor of the Tasks page's header: the same rows as Today's tag button (rename, recolour, delete, each a
/// <see cref="TaskCategoryRowViewModel"/>) and a box that adds one. No lock applies, as on Today: naming a group of tasks
/// loosens nothing, so it works during a focus session too.
/// </summary>
public sealed partial class CategoryEditorViewModel : ObservableObject
{
    private readonly SessionTasksService _tasks;
    private IReadOnlyList<TaskCategory>? _shown;

    public CategoryEditorViewModel(SessionTasksService tasks)
    {
        _tasks = tasks;
        NewCategoryName = string.Empty;
    }

    /// <summary>One row per category.</summary>
    public ObservableCollection<TaskCategoryRowViewModel> Rows { get; } = new();

    [ObservableProperty]
    public partial string NewCategoryName { get; set; }

    /// <summary>No category exists yet: a hint shows.</summary>
    public bool IsEmpty => Rows.Count == 0;

    /// <summary>
    /// The rows are rebuilt only when the categories changed, so a name being typed isn't lost when something else on the page
    /// changes meanwhile.
    /// </summary>
    public void Refresh(IReadOnlyList<TaskCategory> categories)
    {
        if (ReferenceEquals(categories, _shown))
        {
            return;
        }

        _shown = categories;
        Rows.Clear();
        foreach (TaskCategory category in categories)
        {
            Rows.Add(new TaskCategoryRowViewModel(category, _tasks));
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>Enter in the "New category" box: added in the next colour no category has yet.</summary>
    [RelayCommand]
    private async Task AddCategoryAsync()
    {
        string name = NewCategoryName;
        NewCategoryName = string.Empty;
        await _tasks.AddCategoryAsync(name);
    }
}
