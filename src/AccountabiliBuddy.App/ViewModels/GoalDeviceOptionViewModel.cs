using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One device of a screen-time source's "Counts from" list (ST2): this PC or another device of the group; its minutes count when it is ticked.</summary>
public sealed partial class GoalDeviceOptionViewModel : ObservableObject
{
    private readonly Action? _changed;

    public GoalDeviceOptionViewModel(Guid id, string name, MaterialIconKind icon, bool isChecked, bool isEnabled, string? note, Action changed, bool isPc = false)
    {
        IsPc = isPc;
        Id = id;
        Name = name;
        Icon = icon;
        IsChecked = isChecked;
        IsEnabled = isEnabled;
        Note = note;
        _changed = changed; // set last: the first value of IsChecked is not a change
    }

    public Guid Id { get; }

    /// <summary>A PC (counts programs and sites); any other device counts phone apps (ST4 sections).</summary>
    public bool IsPc { get; }

    public string Name { get; }

    public MaterialIconKind Icon { get; }

    /// <summary>Greyed: the device can't be chosen now (the note says why).</summary>
    public bool IsEnabled { get; }

    public string? Note { get; }

    public bool HasNote => Note is not null;

    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    partial void OnIsCheckedChanged(bool value) => _changed?.Invoke();
}
