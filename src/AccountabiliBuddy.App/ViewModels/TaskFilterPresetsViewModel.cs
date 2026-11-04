using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The filter button's Saved section: named sets of filter choices and search words to come back to. Saving under a name that exists
/// (any case) replaces that one. Kept on this PC only (<see cref="TaskFilterPresetStore"/>); nothing here is stored on a task.
/// </summary>
public sealed partial class TaskFilterPresetsViewModel : ObservableObject
{
    private const int MaxNameLength = 40;
    private const int MaxPresets = 30;

    private readonly TaskFilterPresetStore _store;
    private readonly Func<(TaskViewPreferences View, string Search)> _current;
    private readonly Action<TaskFilterPreset> _apply;

    /// <param name="current">What the page shows now, for saving and for lighting the pill that matches.</param>
    /// <param name="apply">Puts a saved filter on the page.</param>
    public TaskFilterPresetsViewModel(TaskFilterPresetStore store, Func<(TaskViewPreferences View, string Search)> current, Action<TaskFilterPreset> apply)
    {
        _store = store;
        _current = current;
        _apply = apply;
        NewName = string.Empty;
        foreach (TaskFilterPreset preset in store.Load())
        {
            Rows.Add(RowOf(preset));
        }
    }

    public ObservableCollection<TaskFilterPresetRowViewModel> Rows { get; } = new();

    public bool IsEmpty => Rows.Count == 0;

    /// <summary>The name box shows ("Save current…" was pressed).</summary>
    [ObservableProperty]
    public partial bool IsNaming { get; set; }

    [ObservableProperty]
    public partial string NewName { get; set; }

    /// <summary>Lights the pill of the saved filter the page shows now, if any.</summary>
    public void Refresh()
    {
        (TaskViewPreferences view, string search) = _current();
        foreach (TaskFilterPresetRowViewModel row in Rows)
        {
            row.IsActive = Same(row.Preset, view, search);
        }
    }

    [RelayCommand]
    private void AskName()
    {
        NewName = string.Empty;
        IsNaming = !IsNaming;
    }

    [RelayCommand]
    private void CancelName()
    {
        NewName = string.Empty;
        IsNaming = false;
    }

    /// <summary>Enter in the name box: keeps what the page shows now under that name. A blank name saves nothing; the list holds <see cref="MaxPresets"/> at most.</summary>
    [RelayCommand]
    private void Save()
    {
        string name = NewName.Trim();
        if (name.Length > MaxNameLength)
        {
            name = name[..MaxNameLength];
        }

        if (name.Length == 0)
        {
            return;
        }

        (TaskViewPreferences view, string search) = _current();
        int index = Rows.ToList().FindIndex(row => string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase));
        if (index < 0 && Rows.Count >= MaxPresets)
        {
            return;
        }

        var preset = new TaskFilterPreset(index >= 0 ? Rows[index].Preset.Id : Guid.NewGuid(), name, view, search);
        if (index >= 0)
        {
            Rows[index] = RowOf(preset);
        }
        else
        {
            Rows.Add(RowOf(preset));
        }

        CancelName();
        Changed();
    }

    private void Delete(TaskFilterPreset preset)
    {
        if (Rows.FirstOrDefault(row => row.Preset.Id == preset.Id) is TaskFilterPresetRowViewModel row)
        {
            Rows.Remove(row);
            Changed();
        }
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(IsEmpty));
        Refresh();
        _ = _store.SaveAsync(Rows.Select(row => row.Preset).ToList());
    }

    private TaskFilterPresetRowViewModel RowOf(TaskFilterPreset preset) => new(preset, _apply, Delete);

    private static bool Same(TaskFilterPreset preset, TaskViewPreferences view, string search)
    {
        TaskViewPreferences saved = preset.View;
        return string.Equals(preset.Search.Trim(), search.Trim(), StringComparison.CurrentCultureIgnoreCase)
            && saved.NoCategory == view.NoCategory
            && saved.Due == view.Due
            && saved.Sort == view.Sort
            && saved.Statuses.ToHashSet().SetEquals(view.Statuses)
            && saved.CategoryIds.ToHashSet().SetEquals(view.CategoryIds);
    }
}
