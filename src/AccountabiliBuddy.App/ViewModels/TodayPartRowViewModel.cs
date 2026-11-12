using AccountabiliBuddy.App.Services.Today;
using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One row of Settings › Today's sidebar list (board PC-Settings-Today): ≡ to drag (or Alt+↑/↓), icon, name, what it is, then Panel /
/// Chip / Off. Outside ProductaBuddy the lunch row offers only Panel and Chip (it always shows while a guard is on).
/// </summary>
public sealed partial class TodayPartRowViewModel : ObservableObject
{
    private static readonly IReadOnlyList<TodayPartPlacement> AllPlaces = [TodayPartPlacement.Panel, TodayPartPlacement.Chip, TodayPartPlacement.Off];
    private static readonly IReadOnlyList<TodayPartPlacement> ShownPlaces = [TodayPartPlacement.Panel, TodayPartPlacement.Chip];

    private readonly Func<TodayPartRowViewModel, TodayPartPlacement, Task> _place;
    private bool _loading;

    public TodayPartRowViewModel(TodayPartInfo info, TodayPartPlacement placement, bool canTurnOff, Func<TodayPartRowViewModel, TodayPartPlacement, Task> place)
    {
        Info = info;
        CanTurnOff = canTurnOff;
        _place = place;
        Places = canTurnOff ? AllPlaces : ShownPlaces;
        PlaceNames = Places.Select(NameOf).ToList();
        _loading = true;
        PlaceIndex = Math.Max(0, Places.ToList().IndexOf(placement == TodayPartPlacement.Off && !canTurnOff ? TodayPartPlacement.Chip : placement));
        _loading = false;
    }

    public TodayPartInfo Info { get; }

    public string Key => Info.Key;

    public string Title => Info.Title;

    public string About => Info.About;

    public MaterialIconKind Icon => Info.Icon;

    /// <summary>Off is offered (not for the lunch row outside ProductaBuddy).</summary>
    public bool CanTurnOff { get; }

    /// <summary>Why Off isn't offered; empty when it is.</summary>
    public string LockNote => CanTurnOff ? string.Empty : "Always shown outside ProductaBuddy while a guard is on";

    public bool HasLockNote => !CanTurnOff;

    public IReadOnlyList<TodayPartPlacement> Places { get; }

    /// <summary>"Panel", "Chip", "Off" (the segmented control's items).</summary>
    public IReadOnlyList<string> PlaceNames { get; }

    [ObservableProperty]
    public partial int PlaceIndex { get; set; }

    public TodayPartPlacement Placement => Places[Math.Clamp(PlaceIndex, 0, Places.Count - 1)];

    partial void OnPlaceIndexChanged(int value)
    {
        if (!_loading && value >= 0 && value < Places.Count)
        {
            _ = _place(this, Places[value]);
        }
    }

    private static string NameOf(TodayPartPlacement placement) => placement switch
    {
        TodayPartPlacement.Panel => "Panel",
        TodayPartPlacement.Chip => "Chip",
        _ => "Off",
    };
}
