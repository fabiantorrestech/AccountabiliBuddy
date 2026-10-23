using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// A linked phone or tablet in the Profiles tab: a row that opens to show the profiles it has (for now this PC's,
/// synced to it).
/// </summary>
public sealed partial class DeviceProfilesRowViewModel : ObservableObject
{
    public DeviceProfilesRowViewModel(Guid id, string name, string detail, IReadOnlyList<string> profileNames)
    {
        Id = id;
        Name = name;
        Detail = detail;
        ProfileNames = profileNames;
    }

    public Guid Id { get; }

    public string Name { get; }

    /// <summary>The device's kind as an icon: a phone, a tablet or a watch.</summary>
    public Material.Icons.MaterialIconKind Icon { get; init; } = Material.Icons.MaterialIconKind.Cellphone;

    /// <summary>E.g. "uses this PC's profiles · connected now".</summary>
    public string Detail { get; }

    public IReadOnlyList<string> ProfileNames { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Arrow))]
    public partial bool IsExpanded { get; set; }

    public string Arrow => IsExpanded ? "▾" : "▸";

    [RelayCommand]
    private void Toggle()
    {
        IsExpanded = !IsExpanded;
    }
}
