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
        ["Over taskbar", "Taskbar left", "Beside tray", "Float right", "Float left"];

    // The placement each entry of PlacementNames stands for. Cover tray is retired, so a list position no longer equals the stored number.
    private static readonly OverlayPlacement[] PlacementChoices =
    [
        OverlayPlacement.OverTaskbar, OverlayPlacement.TaskbarLeft, OverlayPlacement.BesideTray,
        OverlayPlacement.FloatBottomRight, OverlayPlacement.FloatBottomLeft,
    ];

    /// <summary>The name shown for a placement.</summary>
    public static string PlacementName(OverlayPlacement placement)
    {
        return PlacementNames[Array.IndexOf(PlacementChoices, placement)];
    }

    public static IReadOnlyList<string> AutoHideNames { get; } = ["Float", "Slim line", "Hide with it"];

    public static IReadOnlyList<string> LeftAlignedNames { get; } = ["Beside the tray", "Follow my apps"];

    public static IReadOnlyList<string> ShapeNames { get; } = ["Rounded rectangle", "Pill"];

    public static IReadOnlyList<string> SizeNames { get; } = ["Small", "Medium", "Large", "Wide"];

    public static IReadOnlyList<string> ProgressNames { get; } = ["Ring", "Timeline"];

    public static IReadOnlyList<string> PanelSizeNames { get; } = ["Compact", "Standard", "Large"];

    public static IReadOnlyList<string> PeekDelayNames { get; } = ["Instant", "100 ms", "150 ms", "300 ms"];

    private static readonly int[] PeekDelayValues = [0, 100, 150, 300];

    private readonly OverlaySettingsService _settings;
    private readonly SessionController _sessions;
    private readonly Action _turnOn;
    private bool _loading;
    private bool _saving;

    // The list was never saved: the rows show the default for the size and placement and follow them. The first change to a row saves the list.
    private bool _usesDefaultOrder;

    /// <param name="turnOn">The overlay service's turn-on (also forgets that it was turned off from the panel).</param>
    public OverlaySettingsViewModel(
        OverlaySettingsService settings, SessionController sessions, Action turnOn, Func<string?> stickyHotkeyError, Func<string?> hideHotkeyError)
    {
        _settings = settings;
        _sessions = sessions;
        _turnOn = turnOn;
        StickyHotkeyError = stickyHotkeyError() ?? string.Empty;
        HideHotkeyError = HideErrorText(hideHotkeyError());
        InFocus = sessions.IsEnforcing;
        sessions.StateChanged += (_, _) => InFocus = sessions.IsEnforcing;
        Load(settings.Current);
        settings.Changed += (_, current) =>
        {
            // The overlay service has registered (or released) the shortcuts by now; whether they worked is shown either way.
            StickyHotkeyError = stickyHotkeyError() ?? string.Empty;
            HideHotkeyError = HideErrorText(hideHotkeyError());
            if (!_saving)
            {
                Load(current);
            }
        };
        Preview = new OverlayPreviewViewModel(this);
    }

    /// <summary>The live preview beside these settings (display only).</summary>
    public OverlayPreviewViewModel Preview { get; }

    /// <summary>
    /// Raised before a change is saved, with the scene that shows it, and after the settings were reloaded (no scene). The preview follows it;
    /// <see cref="Draft"/> then gives the settings as they would be saved.
    /// </summary>
    public event EventHandler<OverlayPreviewChange>? PreviewChanged;

    /// <summary>The settings as they would be saved with what is chosen now: the saved ones with these choices on top, kept in range like a save does.</summary>
    public OverlaySettings Draft()
    {
        OverlaySettings current = _settings.Current;
        CornerClockParts parts = (ClockSession ? CornerClockParts.Session : 0) | (ClockTime ? CornerClockParts.Time : 0)
                                 | (ClockDate ? CornerClockParts.Date : 0) | (ClockBattery ? CornerClockParts.Battery : 0);
        return _settings.Normalized(current with
        {
            Placement = PlacementIndex >= 0 ? PlacementChoices[PlacementIndex] : current.Placement,
            AutoHide = AutoHideIndex >= 0 ? (AutoHideMode)AutoHideIndex : current.AutoHide,
            LeftAligned = LeftAlignedIndex >= 0 ? (LeftAlignedMode)LeftAlignedIndex : current.LeftAligned,
            CornerClock = CornerClock,
            CornerClockParts = parts,
            Shape = ShapeIndex >= 0 ? (OverlayShape)ShapeIndex : current.Shape,
            Size = SizeIndex >= 0 ? (OverlaySize)SizeIndex : current.Size,
            Progress = ProgressIndex >= 0 ? (OverlayProgress)ProgressIndex : current.Progress,
            ShowSeconds = ShowSeconds,
            PanelSize = PanelSizeIndex >= 0 ? (PanelSize)PanelSizeIndex : current.PanelSize,
            HoldToPeek = HoldToPeek,
            PeekDelayMs = PeekDelayIndex >= 0 ? PeekDelayValues[PeekDelayIndex] : current.PeekDelayMs,
            PanelSticky = PanelSticky,
            PeekKeysLocked = !PeekUsesKeys,
            Opacity = SessionOpacityPercent / 100,
            HeldOpacity = SessionHeldOpacityPercent / 100,
            TimelineOpacity = TimelineOpacityPercent / 100,
            TimelineHeldOpacity = TimelineHeldOpacityPercent / 100,
            ContentOrder = _usesDefaultOrder ? null : Items.Select(row => row.ToItem()).ToList(),
        });
    }

    private void Touch(OverlayPreviewScene scene, bool leftAlignedChosen = false)
    {
        if (!_loading)
        {
            PreviewChanged?.Invoke(this, new OverlayPreviewChange(scene, leftAlignedChosen));
        }
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

    /// <summary>The tile at rest during a session, 20 to 100% (<see cref="OverlaySettings.Opacity"/>).</summary>
    [ObservableProperty]
    public partial double SessionOpacityPercent { get; set; }

    /// <summary>The same while Ctrl+Alt is held (<see cref="OverlaySettings.HeldOpacity"/>).</summary>
    [ObservableProperty]
    public partial double SessionHeldOpacityPercent { get; set; }

    /// <summary>The tile at rest with no session running (<see cref="OverlaySettings.TimelineOpacity"/>).</summary>
    [ObservableProperty]
    public partial double TimelineOpacityPercent { get; set; }

    /// <summary>The same while Ctrl+Alt is held (<see cref="OverlaySettings.TimelineHeldOpacity"/>).</summary>
    [ObservableProperty]
    public partial double TimelineHeldOpacityPercent { get; set; }

    [ObservableProperty]
    public partial int PanelSizeIndex { get; set; }

    [ObservableProperty]
    public partial bool HoldToPeek { get; set; }

    /// <summary>Index into <see cref="PeekDelayNames"/>; -1 (nothing selected) when the saved delay is not one of the four choices.</summary>
    [ObservableProperty]
    public partial int PeekDelayIndex { get; set; }

    [ObservableProperty]
    public partial bool PanelSticky { get; set; }

    /// <summary>The peek may use hide, +10 min, up and down as shortcuts; the inverse of <see cref="OverlaySettings.PeekKeysLocked"/>.</summary>
    [ObservableProperty]
    public partial bool PeekUsesKeys { get; set; }

    /// <summary>Why Ctrl+Alt+Space could not be registered (another copy of the app holds it); empty when it works.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStickyHotkeyError))]
    public partial string StickyHotkeyError { get; set; }

    public bool HasStickyHotkeyError => StickyHotkeyError.Length > 0;

    /// <summary>Why Ctrl+Alt+H could not be registered (another program holds it); empty when it works or is off.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHideHotkeyError))]
    public partial string HideHotkeyError { get; set; }

    public bool HasHideHotkeyError => HideHotkeyError.Length > 0;

    // The shared message ends "Pick a different shortcut", which can't be done for this fixed chord.
    private static string HideErrorText(string? error)
    {
        return error is null ? string.Empty : "Ctrl+Alt+H is already used by another app or by Windows, so it can't hide the overlay. Today's Show button and the tray bring it back.";
    }

    /// <summary>The master switch: off = no tile, timeline or corner clock, no peek, and no Ctrl+Alt+H or Ctrl+Alt+Space.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangeOverlayOn), nameof(ShowsOverlayLockedText))]
    public partial bool OverlayOn { get; set; }

    /// <summary>Ctrl+Alt+H hides the overlay until it is pressed again.</summary>
    [ObservableProperty]
    public partial bool HideShortcutOn { get; set; }

    /// <summary>A focus session the user started is running: the overlay can't be turned off now (turning it on always works), like the panel's Disable.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangeOverlayOn), nameof(ShowsOverlayLockedText))]
    public partial bool InFocus { get; set; }

    public bool CanChangeOverlayOn => !InFocus || !OverlayOn;

    public bool ShowsOverlayLockedText => InFocus && OverlayOn;

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
        OverlayOn = settings.OverlayOn;
        HideShortcutOn = settings.HideShortcutOn;
        PlacementIndex = Array.IndexOf(PlacementChoices, settings.EffectivePlacement);
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
        SessionOpacityPercent = Math.Round(settings.Opacity * 100);
        SessionHeldOpacityPercent = Math.Round(settings.HeldOpacity * 100);
        TimelineOpacityPercent = Math.Round(settings.TimelineOpacity * 100);
        TimelineHeldOpacityPercent = Math.Round(settings.TimelineHeldOpacity * 100);
        PanelSizeIndex = (int)settings.PanelSize;
        HoldToPeek = settings.HoldToPeek;
        PeekDelayIndex = Array.IndexOf(PeekDelayValues, settings.PeekDelayMs);
        PanelSticky = settings.PanelSticky;
        PeekUsesKeys = !settings.PeekKeysLocked;
        UsesBuiltInRules = settings.AppRules is null;
        _usesDefaultOrder = settings.ContentOrder is null;
        SetItems(settings.Items);
        Rules.Clear();
        foreach (OverlayAppRule rule in settings.Rules)
        {
            Rules.Add(Watched(new OverlayRuleRowViewModel(rule)));
        }

        _loading = false;
        PreviewChanged?.Invoke(this, new OverlayPreviewChange(null));
    }

    private void SetItems(IReadOnlyList<OverlayItem> items)
    {
        Items.Clear();
        foreach (OverlayItem item in items)
        {
            var row = new OverlayItemRowViewModel(item);
            row.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(OverlayItemRowViewModel.IsOn))
                {
                    SaveItems();
                }
            };
            Items.Add(row);
        }

        MarkSizeBoundRows();
        UpdatePreview();
    }

    // Time and date and Batteries need Medium or larger (Small is one line); the rows say so and are disabled there.
    private void MarkSizeBoundRows()
    {
        bool small = SizeIndex == (int)OverlaySize.Small;
        foreach (OverlayItemRowViewModel row in Items.Where(row => row.IsSizeBound))
        {
            row.NeedsMedium = small;
        }
    }

    // A size or placement change moves the default list (clock and batteries on for Wide and Cover tray); a saved list stays as it is.
    private void FollowDefaultOrder()
    {
        if (_usesDefaultOrder && !_loading)
        {
            OverlaySettings draft = Draft();
            _loading = true;
            SetItems(draft.Items);
            _loading = false;
        }
        else
        {
            MarkSizeBoundRows();
        }
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

    partial void OnOverlayOnChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        if (value)
        {
            _saving = true;
            _turnOn();
            _saving = false;
            return;
        }

        if (_sessions.IsEnforcing)
        {
            // The same rule as the panel's Disable: not during a focus session. Put the switch back.
            _loading = true;
            OverlayOn = true;
            _loading = false;
            return;
        }

        Save(s => s with { OverlayOn = false, OffFromPanel = false });
    }

    partial void OnHideShortcutOnChanged(bool value)
    {
        // Switching it off while hidden shows the overlay again: with the shortcut gone there would be no way to bring it back.
        Save(s => s with { HideShortcutOn = value, HiddenByShortcut = value && s.HiddenByShortcut });
    }

    partial void OnPlacementIndexChanged(int value)
    {
        FollowDefaultOrder();
        Touch(OverlayPreviewScene.Taskbar);
        if (value < 0)
        {
            return;
        }

        Save(s => s with { Placement = PlacementChoices[value], PlacementChosen = true });
    }

    partial void OnAutoHideIndexChanged(int value)
    {
        Touch(OverlayPreviewScene.AutoHide);
        Save(s => s with { AutoHide = (AutoHideMode)value });
    }

    partial void OnLeftAlignedIndexChanged(int value)
    {
        Touch(OverlayPreviewScene.Taskbar, leftAlignedChosen: true);
        Save(s => s with { LeftAligned = (LeftAlignedMode)value });
    }

    partial void OnCornerClockChanged(bool value)
    {
        Touch(OverlayPreviewScene.FullScreen);
        Save(s => s with { CornerClock = value });
    }

    partial void OnClockSessionChanged(bool value) => SaveClockParts();

    partial void OnClockTimeChanged(bool value) => SaveClockParts();

    partial void OnClockDateChanged(bool value) => SaveClockParts();

    partial void OnClockBatteryChanged(bool value) => SaveClockParts();

    partial void OnShapeIndexChanged(int value)
    {
        Touch(OverlayPreviewScene.Taskbar);
        Save(s => s with { Shape = (OverlayShape)value });
    }

    partial void OnSizeIndexChanged(int value)
    {
        FollowDefaultOrder();
        Touch(OverlayPreviewScene.Taskbar);
        Save(s => s with { Size = (OverlaySize)value });
    }

    partial void OnProgressIndexChanged(int value)
    {
        Touch(OverlayPreviewScene.Taskbar);
        Save(s => s with { Progress = (OverlayProgress)value });
    }

    partial void OnShowSecondsChanged(bool value)
    {
        Touch(OverlayPreviewScene.Taskbar);
        Save(s => s with { ShowSeconds = value });
    }

    partial void OnSessionOpacityPercentChanged(double value)
    {
        Touch(OverlayPreviewScene.Taskbar);
        Save(s => s with { Opacity = value / 100 });
    }

    partial void OnSessionHeldOpacityPercentChanged(double value)
    {
        Save(s => s with { HeldOpacity = value / 100 });
    }

    partial void OnTimelineOpacityPercentChanged(double value)
    {
        Touch(OverlayPreviewScene.Taskbar);
        Save(s => s with { TimelineOpacity = value / 100 });
    }

    partial void OnTimelineHeldOpacityPercentChanged(double value)
    {
        Save(s => s with { TimelineHeldOpacity = value / 100 });
    }

    partial void OnPanelSizeIndexChanged(int value)
    {
        Touch(OverlayPreviewScene.Panel);
        Save(s => s with { PanelSize = (PanelSize)value });
    }

    partial void OnHoldToPeekChanged(bool value)
    {
        Touch(OverlayPreviewScene.Panel);
        Save(s => s with { HoldToPeek = value });
    }

    partial void OnPeekDelayIndexChanged(int value)
    {
        if (value >= 0)
        {
            Touch(OverlayPreviewScene.Panel);
            Save(s => s with { PeekDelayMs = PeekDelayValues[value] });
        }
    }

    partial void OnPanelStickyChanged(bool value)
    {
        Touch(OverlayPreviewScene.Panel);
        Save(s => s with { PanelSticky = value });
    }

    partial void OnPeekUsesKeysChanged(bool value)
    {
        Touch(OverlayPreviewScene.Panel);
        Save(s => s with { PeekKeysLocked = !value });
    }

    private void SaveClockParts()
    {
        CornerClockParts parts = (ClockSession ? CornerClockParts.Session : 0) | (ClockTime ? CornerClockParts.Time : 0)
                                 | (ClockDate ? CornerClockParts.Date : 0) | (ClockBattery ? CornerClockParts.Battery : 0);
        Touch(OverlayPreviewScene.FullScreen);
        Save(s => s with { CornerClockParts = parts });
    }

    private void SaveItems()
    {
        _usesDefaultOrder = false;
        UpdatePreview();
        Touch(OverlayPreviewScene.Taskbar);
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
    private void ResetOrder() => _ = _settings.SaveLookAsync(s => s with { ContentOrder = null });

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
