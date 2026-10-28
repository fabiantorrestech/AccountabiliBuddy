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

    public SettingsSection Section { get; }

    /// <summary>The row's place in the list (what a search result inside it opens).</summary>
    public int Index => (int)Section;

    public string Name { get; }

    public MaterialIconKind Icon { get; }

    /// <summary>The Muted line under the page's title.</summary>
    public string Summary { get; }

    /// <summary>A little air above it: the rows under the gap are App &amp; data, Setup and About.</summary>
    public bool GapBefore { get; }

    public Avalonia.Thickness RowMargin => GapBefore ? new Avalonia.Thickness(0, 14, 0, 0) : new Avalonia.Thickness(0);

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Shown while the Settings search field has no filter, or this row's name or content matches it.</summary>
    [ObservableProperty]
    public partial bool IsListed { get; set; } = true;

    /// <summary>The lock on the right of the row: its guard parts are resting (view-only) while a focus session runs.</summary>
    [ObservableProperty]
    public partial bool IsResting { get; set; }
}
