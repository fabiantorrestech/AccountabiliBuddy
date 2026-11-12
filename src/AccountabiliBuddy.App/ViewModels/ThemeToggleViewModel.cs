using AccountabiliBuddy.App.Services.Theming;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The title bar's light/dark toggle (round 2, brief §3.2; ADR 49). A click flips what is shown between light and dark (sets
/// <see cref="LightDarkMode.Light"/> or <see cref="LightDarkMode.Dark"/>); its menu picks the mode (Light, Dark, Match Windows,
/// At a set time, After sunset) and the theme (Instrument, Night Desk, Paper), or opens Settings › Appearance. Everything goes
/// through <see cref="AppTheme"/>, which writes only this PC's "theme" document: never synced. Appearance isn't a guard setting,
/// so the toggle works during a focus session too; "Theme settings…" opens Settings, whose own session locks apply there.
/// </summary>
public sealed partial class ThemeToggleViewModel : ObservableObject
{
    public const string ToolTipText = "Light / dark · right-click or hold for themes";

    private readonly AppTheme _theme;

    public ThemeToggleViewModel(AppTheme theme)
    {
        _theme = theme;
        _theme.Changed += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>Opens Settings › Appearance (set by the main window's view model).</summary>
    public Action? OpenThemeSettings { get; set; }

    /// <summary>The app is showing its dark variant now (set by the view from the theme it actually shows, Windows' too).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Icon), nameof(AccessibleName))]
    public partial bool IsDarkShown { get; set; }

    /// <summary>What a click switches to: the sun while dark is shown, the moon while light is.</summary>
    public MaterialIconKind Icon => IsDarkShown ? MaterialIconKind.WhiteBalanceSunny : MaterialIconKind.MoonWaningCrescent;

    public string ToolTip => ToolTipText;

    public string AccessibleName => IsDarkShown
        ? "Switch to light. Right-click or hold for themes"
        : "Switch to dark. Right-click or hold for themes";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLight), nameof(IsDark), nameof(IsSystem), nameof(IsSetTime), nameof(IsSunset))]
    public partial LightDarkMode Mode { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstrument), nameof(IsNightDesk), nameof(IsPaper))]
    public partial ThemeName Theme { get; private set; }

    public bool IsLight => Mode == LightDarkMode.Light;

    public bool IsDark => Mode == LightDarkMode.Dark;

    public bool IsSystem => Mode == LightDarkMode.System;

    public bool IsSetTime => Mode == LightDarkMode.SetTime;

    public bool IsSunset => Mode == LightDarkMode.Sunset;

    public bool IsInstrument => Theme == ThemeName.Instrument;

    public bool IsNightDesk => Theme == ThemeName.NightDesk;

    public bool IsPaper => Theme == ThemeName.Paper;

    /// <summary>The click: dark if light is shown, light if dark is shown.</summary>
    [RelayCommand]
    private Task ToggleAsync()
    {
        return _theme.SetModeAsync(IsDarkShown ? LightDarkMode.Light : LightDarkMode.Dark);
    }

    /// <summary>A mode from the menu. After sunset with no place known yet also opens Settings › Appearance to set one.</summary>
    [RelayCommand]
    private async Task SetModeAsync(LightDarkMode mode)
    {
        await _theme.SetModeAsync(mode);
        if (NeedsSettings(mode))
        {
            OpenThemeSettings?.Invoke();
        }
    }

    [RelayCommand]
    private Task SetThemeAsync(ThemeName theme)
    {
        return _theme.SetThemeAsync(theme);
    }

    [RelayCommand]
    private void OpenSettings()
    {
        OpenThemeSettings?.Invoke();
    }

    /// <summary>
    /// True when a mode can't work out light and dark by itself: After sunset with no place (it falls back on the set times).
    /// At a set time always has times (7 PM to 7 AM until changed), so it never needs Settings.
    /// </summary>
    public bool NeedsSettings(LightDarkMode mode) => mode == LightDarkMode.Sunset && _theme.Place is null;

    private void Refresh()
    {
        Mode = _theme.Mode;
        Theme = _theme.Theme;
    }
}
