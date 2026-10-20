using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One group in the Settings side list (e.g. "Your day") with the sections it holds; one group shows at a time.</summary>
public sealed partial class SettingsGroupViewModel : ObservableObject
{
    public SettingsGroupViewModel(string name, IReadOnlyList<string> sections)
    {
        Name = name;
        Sections = sections;
    }

    public string Name { get; }

    /// <summary>The titles of its cards, in order; clicking one in the side list scrolls to it.</summary>
    public IReadOnlyList<string> Sections { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
