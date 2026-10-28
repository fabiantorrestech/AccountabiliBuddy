using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The "PC overlay" settings (15c, 14c): where it lives, how it looks, what it shows and in what order, the Ctrl+Alt panel, the corner clock and
/// the per-app rules. Each change is saved at once through <see cref="OverlaySettingsService.SaveLookAsync"/>; the overlay follows. Display only:
/// nothing here loosens anything, so nothing waits. Use from the UI thread only.
/// </summary>
public sealed partial class OverlaySettingsViewModel : ObservableObject
{
    public static IReadOnlyList<string> PlacementNames { get; } =
        ["Docked over taskbar", "Docked, covering tray", "Taskbar left", "Beside tray", "Floating bottom right", "Floating bottom left"];

    public static IReadOnlyList<string> AutoHideNames { get; } = ["Float", "Slim line", "Hide with it"];

    public static IReadOnlyList<string> LeftAlignedNames { get; } = ["Beside the tray", "Follow my apps"];

    public static IReadOnlyList<string> ShapeNames { get; } = ["Rounded rectangle", "Pill"];

    public static IReadOnlyList<string> SizeNames { get; } = ["Small", "Medium", "Large", "Wide"];

    public static IReadOnlyList<string> ProgressNames { get; } = ["Ring", "Timeline"];

    public static IReadOnlyList<string> PanelSizeNames { get; } = ["Compact", "Standard", "Large"];

    private readonly OverlaySettingsService _settings;
    private bool _loading;
    private bool _saving;

    public OverlaySettingsViewModel(OverlaySettingsService settings, Func<string?> stickyHotkeyError)
    {
        _settings = settings;
        StickyHotkeyError = stickyHotkeyError() ?? string.Empty;
        Load(settings.Current);
        settings.Changed += (_, current) =>
        {
            if (!_saving)
            {
                Load(current);
            }
        };
    }

    [ObservableProperty]
    public partial int PlacementIndex { get; set; }

    [ObservableProperty]
    public partial int AutoHideIndex { get; set; }

    [ObservableProperty]
    public partial int LeftAlignedIndex { get; set; }

    [ObservableProperty]
    public partial bool CornerClock { get; set; }

    [ObservableProperty]
    public partial bool ClockSession { get; set; }

    [ObservableProperty]
    public partial bool ClockTime { get; set; }

    [ObservableProperty]
    public partial bool ClockDate { get; set; }

    [ObservableProperty]
    public partial bool ClockBattery { get; set; }

    [ObservableProperty]
    public partial int ShapeIndex { get; set; }

    [ObservableProperty]
    public partial int SizeIndex { get; set; }

    [ObservableProperty]
    public partial int ProgressIndex { get; set; }

    [ObservableProperty]
    public partial bool ShowSeconds { get; set; }

    [ObservableProperty]
    public partial int PanelSizeIndex { get; set; }

    [ObservableProperty]
    public partial bool HoldToPeek { get; set; }

    [ObservableProperty]
    public partial bool PanelSticky { get; set; }

    /// <summary>Why Ctrl+Alt+Space could not be registered (another copy of the app holds it); empty when it works.</summary>
    public string StickyHotkeyError { get; }

    public bool HasStickyHotkeyError => StickyHotkeyError.Length > 0;

    public ObservableCollection<OverlayItemRowViewModel> Items { get; } = [];

    public ObservableCollection<OverlayRuleRowViewModel> Rules { get; } = [];

    /// <summary>The order as a line: "Ring · Task · Pause and done", off items left out.</summary>
    [ObservableProperty]
    public partial string PreviewLine { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsOrderOpen { get; set; }

    [ObservableProperty]
    public partial bool UsesBuiltInRules { get; set; }

    private void Load(OverlaySettings settings)
    {
        _loading = true;
        PlacementIndex = (int)settings.EffectivePlacement;
        AutoHideIndex = (int)settings.AutoHide;
        LeftAlignedIndex = (int)settings.LeftAligned;
        CornerClock = settings.CornerClock;
        ClockSession = settings.CornerClockParts.HasFlag(CornerClockParts.Session);
        ClockTime = settings.CornerClockParts.HasFlag(CornerClockParts.Time);
        ClockDate = settings.CornerClockParts.HasFlag(CornerClockParts.Date);
        ClockBattery = settings.CornerClockParts.HasFlag(CornerClockParts.Battery);
        ShapeIndex = (int)settings.Shape;
        SizeIndex = (int)settings.Size;
        ProgressIndex = (int)settings.Progress;
        ShowSeconds = settings.ShowSeconds;
        PanelSizeIndex = (int)settings.PanelSize;
        HoldToPeek = settings.HoldToPeek;
        PanelSticky = settings.PanelSticky;
        UsesBuiltInRules = settings.AppRules is null;
        SetItems(settings.Items);
        Rules.Clear();
        foreach (OverlayAppRule rule in settings.Rules)
        {
            Rules.Add(Watched(new OverlayRuleRowViewModel(rule)));
        }

        _loading = false;
    }

    private void SetItems(IReadOnlyList<OverlayItem> items)
    {
        Items.Clear();
        foreach (OverlayItem item in items)
        {
            var row = new OverlayItemRowViewModel(item);
            row.PropertyChanged += (_, _) => SaveItems();
            Items.Add(row);
        }

        UpdatePreview();
    }

    private OverlayRuleRowViewModel Watched(OverlayRuleRowViewModel row)
    {
        row.PropertyChanged += (_, _) => SaveRules();
        return row;
    }

    private void Save(Func<OverlaySettings, OverlaySettings> change)
    {
        if (!_loading)
        {
            _saving = true;
            _ = _settings.SaveLookAsync(change);
            _saving = false;
        }
    }

    partial void OnPlacementIndexChanged(int value) => Save(s => s with { Placement = (OverlayPlacement)value });

    partial void OnAutoHideIndexChanged(int value) => Save(s => s with { AutoHide = (AutoHideMode)value });

    partial void OnLeftAlignedIndexChanged(int value) => Save(s => s with { LeftAligned = (LeftAlignedMode)value });

    partial void OnCornerClockChanged(bool value) => Save(s => s with { CornerClock = value });

    partial void OnClockSessionChanged(bool value) => SaveClockParts();

    partial void OnClockTimeChanged(bool value) => SaveClockParts();

    partial void OnClockDateChanged(bool value) => SaveClockParts();

    partial void OnClockBatteryChanged(bool value) => SaveClockParts();

    partial void OnShapeIndexChanged(int value) => Save(s => s with { Shape = (OverlayShape)value });

    partial void OnSizeIndexChanged(int value) => Save(s => s with { Size = (OverlaySize)value });

    partial void OnProgressIndexChanged(int value) => Save(s => s with { Progress = (OverlayProgress)value });

    partial void OnShowSecondsChanged(bool value) => Save(s => s with { ShowSeconds = value });

    partial void OnPanelSizeIndexChanged(int value) => Save(s => s with { PanelSize = (PanelSize)value });

    partial void OnHoldToPeekChanged(bool value) => Save(s => s with { HoldToPeek = value });

    partial void OnPanelStickyChanged(bool value) => Save(s => s with { PanelSticky = value });

    private void SaveClockParts()
    {
        CornerClockParts parts = (ClockSession ? CornerClockParts.Session : 0) | (ClockTime ? CornerClockParts.Time : 0)
                                 | (ClockDate ? CornerClockParts.Date : 0) | (ClockBattery ? CornerClockParts.Battery : 0);
        Save(s => s with { CornerClockParts = parts });
    }

    private void SaveItems()
    {
        UpdatePreview();
        Save(s => s with { ContentOrder = Items.Select(row => row.ToItem()).ToList() });
    }

    private void UpdatePreview()
    {
        PreviewLine = string.Join(" · ", Items.Where(row => row.IsOn).Select(row => row.Name));
    }

    private void SaveRules()
    {
        UsesBuiltInRules = false;
        Save(s => s with { AppRules = Rules.Where(row => row.ProcessName.Trim().Length > 0).Select(row => row.ToRule()).ToList() });
    }

    [RelayCommand]
    private void ToggleOrder()
    {
        IsOrderOpen = !IsOrderOpen;
    }

    [RelayCommand]
    private void MoveUp(OverlayItemRowViewModel row)
    {
        Move(row, -1);
    }

    [RelayCommand]
    private void MoveDown(OverlayItemRowViewModel row)
    {
        Move(row, 1);
    }

    private void Move(OverlayItemRowViewModel row, int offset)
    {
        int index = Items.IndexOf(row);
        int target = index + offset;
        if (index < 0 || target < 0 || target >= Items.Count)
        {
            return;
        }

        Items.Move(index, target);
        SaveItems();
    }

    [RelayCommand]
    private void FocusFirst() => ApplyPreset(OverlayContentOrder.FocusFirst);

    [RelayCommand]
    private void ClockFirst() => ApplyPreset(OverlayContentOrder.ClockFirst);

    [RelayCommand]
    private void JustTheEssentials() => ApplyPreset(OverlayContentOrder.Essentials);

    [RelayCommand]
    private void ResetOrder() => ApplyPreset(OverlayContentOrder.FocusFirst);

    private void ApplyPreset(IReadOnlyList<OverlayItem> preset)
    {
        _ = _settings.SaveLookAsync(s => s with { ContentOrder = preset.ToList() });
    }

    [RelayCommand]
    private void AddRule()
    {
        Rules.Add(Watched(new OverlayRuleRowViewModel(new OverlayAppRule(string.Empty))));
    }

    [RelayCommand]
    private void RemoveRule(OverlayRuleRowViewModel row)
    {
        Rules.Remove(row);
        SaveRules();
    }

    [RelayCommand]
    private void UseBuiltInRules()
    {
        _ = _settings.SaveLookAsync(s => s with { AppRules = null });
    }
}
