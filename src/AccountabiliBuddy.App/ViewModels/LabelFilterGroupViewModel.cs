using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// A category of the Tasks page's +N menu (Tasks redesign 4b, ADR 93): its icon, name and label count over its labels in the user's order;
/// "Other labels" (no <see cref="GroupId"/>) holds the labels without a category and always comes last (it can't be moved).
/// </summary>
public sealed partial class LabelFilterGroupViewModel : ObservableObject
{
    public LabelFilterGroupViewModel(Guid? groupId)
    {
        GroupId = groupId;
        Name = CountText = string.Empty;
    }

    /// <summary>The label group's id; null for "Other labels".</summary>
    public Guid? GroupId { get; }

    /// <summary>A real category: its heading drags (and Alt+↑/↓ moves it) to move it with all its labels.</summary>
    public bool CanMove => GroupId is not null;

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial MaterialIconKind Icon { get; set; }

    /// <summary>"7": how many labels it holds.</summary>
    [ObservableProperty]
    public partial string CountText { get; set; }

    /// <summary>Its labels, in the user's order.</summary>
    public ObservableCollection<LabelFilterChipViewModel> Items { get; } = [];
}
