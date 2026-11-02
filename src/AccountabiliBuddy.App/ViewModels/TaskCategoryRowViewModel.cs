using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A task category in the checklist's "Categories" panel: rename it, pick one of the 8 colours, or delete it.</summary>
public sealed partial class TaskCategoryRowViewModel : ObservableObject
{
    private readonly SessionTasksService _tasks;
    private string _savedName;
    private readonly bool _ready;

    public TaskCategoryRowViewModel(TaskCategory category, SessionTasksService tasks)
    {
        _tasks = tasks;
        Id = category.Id;
        _savedName = category.Name;
        Name = category.Name;
        Colors = TaskCategory.Palette.Any(hex => string.Equals(hex, category.Color, StringComparison.OrdinalIgnoreCase))
            ? Palette
            : [.. Palette, new ColorChoice(category.Color)];
        SelectedColor = Colors.First(choice => string.Equals(choice.Hex, category.Color, StringComparison.OrdinalIgnoreCase));
        _ready = true;
    }

    /// <summary>The 8 colours a task category can have.</summary>
    public static IReadOnlyList<ColorChoice> Palette { get; } = TaskCategory.Palette.Select(hex => new ColorChoice(hex)).ToList();

    public Guid Id { get; }

    [ObservableProperty]
    public partial string Name { get; set; }

    /// <summary>The palette, plus this category's own colour when it isn't one of them.</summary>
    public IReadOnlyList<ColorChoice> Colors { get; }

    [ObservableProperty]
    public partial ColorChoice SelectedColor { get; set; }

    /// <summary>Enter or clicking away: the new name is kept (a blank one keeps the old name).</summary>
    [RelayCommand]
    private Task SaveNameAsync()
    {
        string trimmed = Name.Trim();
        if (trimmed.Length == 0 || trimmed == _savedName)
        {
            return Task.CompletedTask;
        }

        // Saved once: Enter, then the box losing focus as the panel is rebuilt, doesn't save it again.
        _savedName = trimmed;
        return _tasks.RenameCategoryAsync(Id, trimmed);
    }

    /// <summary>The question that shows after the ✕: "Delete Work? 12 items lose this category, on all your devices."</summary>
    [ObservableProperty]
    public partial string ConfirmText { get; set; } = string.Empty;

    /// <summary>The delete question is showing (Delete / Keep it).</summary>
    [ObservableProperty]
    public partial bool IsConfirmingDelete { get; set; }

    /// <summary>The ✕: asks first, always, saying how many tasks, Later items and come-backs lose the category.</summary>
    [RelayCommand]
    private void AskDelete()
    {
        int count = _tasks.CategoryUseCount(Id);
        string effect = count switch
        {
            0 => "Nothing uses it.",
            1 => "1 item loses this category, on all your devices.",
            _ => $"{count} items lose this category, on all your devices.",
        };
        ConfirmText = $"Delete {_savedName}? {effect}";
        IsConfirmingDelete = true;
    }

    [RelayCommand]
    private void CancelDelete()
    {
        IsConfirmingDelete = false;
    }

    /// <summary>The answer yes: the category goes, and with it from its tasks, Later items and come-backs.</summary>
    [RelayCommand]
    private Task DeleteAsync()
    {
        return _tasks.DeleteCategoryAsync(Id);
    }

    partial void OnSelectedColorChanged(ColorChoice value)
    {
        if (_ready)
        {
            _ = _tasks.RecolorCategoryAsync(Id, value.Hex);
        }
    }
}
