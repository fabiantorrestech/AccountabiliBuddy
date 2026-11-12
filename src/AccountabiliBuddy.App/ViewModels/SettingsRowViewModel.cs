using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One row in the Settings list (e.g. "Sleep"); one row's content shows at a time.</summary>
public sealed partial class SettingsRowViewModel : ObservableObject
{
    public SettingsRowViewModel(SettingsSection section, string name, MaterialIconKind icon, string summary, bool gapBefore = false)
    {
        Section = section;
        Name = name;
        Icon = icon;
        Summary = summary;
        GapBefore = gapBefore;
    }

    /// <summary>Which page the row opens (what a search result inside it opens too).</summary>
    public SettingsSection Section { get; }

    public string Name { get; }

    public MaterialIconKind Icon { get; }

    /// <summary>The Muted line under the page's title (redone when it names the app and the mode's name changes).</summary>
    [ObservableProperty]
    public partial string Summary { get; set; }

    /// <summary>A little air above it: the rows under the gap are App &amp; data, Setup and About.</summary>
    public bool GapBefore { get; }

    public Avalonia.Thickness RowMargin => GapBefore ? new Avalonia.Thickness(0, 14, 0, 0) : new Avalonia.Thickness(0);

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Shown while the Settings search field has no filter, or this row's name or content matches it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShownInList))]
    public partial bool IsListed { get; set; } = true;

    /// <summary>The page exists on this PC (Tasks needs the task list, ADR 76); false hides the row whatever the search.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShownInList))]
    public partial bool IsAvailable { get; set; } = true;

    /// <summary>The row shows in the list: available and not filtered out by the search.</summary>
    public bool IsShownInList => IsAvailable && IsListed;

    /// <summary>The lock on the right of the row: its guard parts are resting (view-only) while a focus session runs.</summary>
    [ObservableProperty]
    public partial bool IsResting { get; set; }
}
