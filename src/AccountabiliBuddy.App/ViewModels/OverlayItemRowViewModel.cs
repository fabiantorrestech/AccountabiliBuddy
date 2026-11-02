using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One line of the "what the overlay shows, and in what order" list: its name, a sample and a switch.</summary>
public sealed partial class OverlayItemRowViewModel : ObservableObject
{
    public OverlayItemRowViewModel(OverlayItem item)
    {
        Kind = item.Kind;
        IsOn = item.Enabled;
        (Name, Sample) = item.Kind switch
        {
            OverlayItemKind.Ring => ("Ring or timeline", "drawn as the Progress setting says"),
            OverlayItemKind.Minutes => ("Minutes", "18 min"),
            OverlayItemKind.Task => ("Task name", "Draft Q4 outline"),
            OverlayItemKind.Controls => ("Pause and done", "pause, check"),
            OverlayItemKind.TimeDate => ("Time and date", "1:38 PM · Tue, Oct 27"),
            OverlayItemKind.Batteries => ("Batteries", "this PC 82%"),
            _ => ("Sound", "the noise player"),
        };
    }

    public OverlayItemKind Kind { get; }

    public string Name { get; }

    public string Sample { get; }

    /// <summary>True for Time and date and Batteries at Small, where the tile stays one line.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAvailable), nameof(Detail))]
    public partial bool NeedsMedium { get; set; }

    public bool IsAvailable => !NeedsMedium;

    /// <summary>The sample, or why the switch is disabled.</summary>
    public string Detail => NeedsMedium ? "Needs Medium or larger" : Sample;

    public bool IsSizeBound => Kind is OverlayItemKind.TimeDate or OverlayItemKind.Batteries;

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    public OverlayItem ToItem() => new(Kind, IsOn);
}
