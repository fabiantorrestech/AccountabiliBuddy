using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One segment of the Stats page's Day / Week / Month / Year control.</summary>
public sealed partial class StatsRangeOptionViewModel : ObservableObject
{
    public StatsRangeOptionViewModel(StatsRange range, string text)
    {
        Range = range;
        Text = text;
    }

    public StatsRange Range { get; }

    public string Text { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>False when the shown view has no such range.</summary>
    [ObservableProperty]
    public partial bool IsShown { get; set; }
}
