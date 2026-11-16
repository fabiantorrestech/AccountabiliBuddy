using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One ticked device in a screen-time entry's "Devices" chip (ST4): the entry counts on it while this is ticked.</summary>
public sealed partial class GoalEntryDeviceChoiceViewModel : ObservableObject
{
    private readonly Action<GoalEntryDeviceChoiceViewModel>? _changed;

    public GoalEntryDeviceChoiceViewModel(Guid id, string name, MaterialIconKind icon, bool isChecked, Action<GoalEntryDeviceChoiceViewModel> changed)
    {
        Id = id;
        Name = name;
        Icon = icon;
        IsChecked = isChecked;
        _changed = changed; // set last: the first value of IsChecked is not a change
    }

    public Guid Id { get; }

    public string Name { get; }

    public MaterialIconKind Icon { get; }

    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    partial void OnIsCheckedChanged(bool value) => _changed?.Invoke(this);
}
