using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One device in the battery matrix (design 16a): where its battery shows (collapsed overlay, Ctrl+Alt panel, the app). A device
/// whose battery level isn't known shows "no battery info" and its boxes are disabled; the saved ticks still show.
/// </summary>
public sealed partial class BatteryRowViewModel : ObservableObject
{
    private readonly BatteryVisibilityService _service;
    private bool _loading = true;

    public BatteryRowViewModel(string key, string name, MaterialIconKind icon, int? batteryPercent, BatteryPlaces places, BatteryVisibilityService service)
    {
        Key = key;
        Name = name;
        Icon = icon;
        BatteryPercent = batteryPercent;
        _service = service;
        CollapsedOverlay = places.CollapsedOverlay;
        CtrlAltPanel = places.CtrlAltPanel;
        InApp = places.InApp;
        _loading = false;
    }

    public string Key { get; }

    public string Name { get; }

    public MaterialIconKind Icon { get; }

    /// <summary>The device's battery level, when it reports one; null for "no battery info".</summary>
    public int? BatteryPercent { get; }

    public bool HasBattery => BatteryPercent is not null;

    public string BatteryText => BatteryPercent is int percent ? $"{percent}%" : "no battery info";

    [ObservableProperty]
    public partial bool CollapsedOverlay { get; set; }

    [ObservableProperty]
    public partial bool CtrlAltPanel { get; set; }

    [ObservableProperty]
    public partial bool InApp { get; set; }

    partial void OnCollapsedOverlayChanged(bool value) => Save();

    partial void OnCtrlAltPanelChanged(bool value) => Save();

    partial void OnInAppChanged(bool value) => Save();

    private void Save()
    {
        if (!_loading)
        {
            _ = _service.SetPlacesAsync(Key, new BatteryPlaces(CollapsedOverlay, CtrlAltPanel, InApp));
        }
    }
}
