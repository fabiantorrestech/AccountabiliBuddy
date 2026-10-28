using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A chip of the Stats page's row: one view. The selected one is Ink on Surface2.</summary>
public sealed partial class StatsChipViewModel : ObservableObject
{
    public StatsChipViewModel(StatsViewKind kind, string text)
    {
        Kind = kind;
        Text = text;
    }

    public StatsViewKind Kind { get; }

    public string Text { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>False hides the chip (Sleep and Phone, with no data).</summary>
    [ObservableProperty]
    public partial bool IsShown { get; set; } = true;
}
