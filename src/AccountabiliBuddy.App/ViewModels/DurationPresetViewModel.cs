using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One preset button in the quick-session popup, e.g. "25". Highlighted when it matches the slider.</summary>
public sealed partial class DurationPresetViewModel : ObservableObject
{
    public DurationPresetViewModel(int minutes, int shortcutNumber)
    {
        Minutes = minutes;
        ShortcutNumber = shortcutNumber;
    }

    public int Minutes { get; }

    /// <summary>The number key that picks this preset.</summary>
    public int ShortcutNumber { get; }

    public string Label => Minutes.ToString();

    public string ToolTip => $"{Minutes} minutes (press {ShortcutNumber})";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
