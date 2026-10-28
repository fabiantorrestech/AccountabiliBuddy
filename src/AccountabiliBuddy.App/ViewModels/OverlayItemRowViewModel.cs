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
            OverlayItemKind.Ring => ("Ring and minutes", "18 min"),
            OverlayItemKind.Task => ("Task name", "Draft Q4 outline"),
            OverlayItemKind.Timeline => ("Timeline", "done · left · break · next"),
            OverlayItemKind.Controls => ("Pause and done", "pause, check"),
            OverlayItemKind.TimeDate => ("Time and date", "1:38 PM · Tue, Oct 27"),
            OverlayItemKind.Batteries => ("Batteries", "this PC 82%"),
            _ => ("Sound", "the noise player"),
        };
    }

    public OverlayItemKind Kind { get; }

    public string Name { get; }

    public string Sample { get; }

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    public OverlayItem ToItem() => new(Kind, IsOn);
}
