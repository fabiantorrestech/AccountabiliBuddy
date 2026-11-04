using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One saved filter in the filter button's Saved section: a pill that applies it (lit while the page shows exactly it) and a ✕ that deletes it.</summary>
public sealed partial class TaskFilterPresetRowViewModel : ObservableObject
{
    private readonly Action<TaskFilterPreset> _apply;
    private readonly Action<TaskFilterPreset> _delete;

    public TaskFilterPresetRowViewModel(TaskFilterPreset preset, Action<TaskFilterPreset> apply, Action<TaskFilterPreset> delete)
    {
        Preset = preset;
        _apply = apply;
        _delete = delete;
    }

    public TaskFilterPreset Preset { get; }

    public string Name => Preset.Name;

    public string DeleteName => $"Delete saved filter {Preset.Name}";

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    [RelayCommand]
    private void Apply() => _apply(Preset);

    [RelayCommand]
    private void Delete() => _delete(Preset);
}
