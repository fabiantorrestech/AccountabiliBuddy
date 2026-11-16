using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One ticked device's line under a screen-time source (ST4): the apps and sites that count there, or that none does.</summary>
public sealed class GoalDeviceSectionViewModel
{
    public GoalDeviceSectionViewModel(string name, MaterialIconKind icon, string text, bool isEmpty)
    {
        Name = name;
        Icon = icon;
        Text = text;
        IsEmpty = isEmpty;
    }

    public string Name { get; }

    public MaterialIconKind Icon { get; }

    /// <summary>The entries that count on the device (comma separated), or "Nothing counts here".</summary>
    public string Text { get; }

    /// <summary>No entry counts on the device (shown as a warning).</summary>
    public bool IsEmpty { get; }
}
