using System.Collections.ObjectModel;
using System.ComponentModel;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.Services.Theming;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The title-bar menu (Tasks redesign 9, SPEC "Title bar menu (one button)"): one button with the sync dot opens Search, Sync (the status
/// row folding out each device, today's QR codes, Sync now) and Appearance (Light · Dark · System, the Theme row folding out the themes).
/// It replaces the sync light, Sync now, the light/dark toggle and the Search box, and works through their view models: the window's search
/// (<see cref="SearchViewModel"/>), the sync light (<see cref="SyncLightViewModel"/>), the window's refresh and the theme
/// (<see cref="ThemeToggleViewModel"/>, this PC's "theme" document only). Which sections are unfolded is this PC's own (document "syncLight").
/// </summary>
public sealed partial class TitleMenuViewModel : ObservableObject
{
    public const string AccessibleName = "Search, sync and appearance";

    // "Synced just now" stays this long after a sync, then the row reads "Sync now" again.
    private static readonly TimeSpan SyncedJustNowFor = TimeSpan.FromMinutes(1);

    private readonly SyncLightSettingService? _folds;
    private readonly DispatcherTimer _syncedTimer = new() { Interval = SyncedJustNowFor };
    private bool _syncedJustNow;
    private bool _wasRefreshing;

    public TitleMenuViewModel(MainWindowViewModel main, SyncLightSettingService? folds)
    {
        Main = main;
        _folds = folds;
        IsDevicesOpen = folds?.DevicesOpen ?? true;
        IsThemesOpen = folds?.ThemesOpen ?? false;
        _wasRefreshing = main.IsRefreshing;
        main.PropertyChanged += OnMainChanged;
        _syncedTimer.Tick += (_, _) =>
        {
            _syncedTimer.Stop();
            _syncedJustNow = false;
            OnPropertyChanged(nameof(SyncNowText));
        };
        if (main.SyncLight is { } light)
        {
            light.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(SyncLightViewModel.TipText) or nameof(SyncLightViewModel.IsVisible))
                {
                    OnPropertyChanged(nameof(ButtonTip));
                }
            };
        }

        if (main.Search is { } search)
        {
            search.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SearchViewModel.HasQuery) || e.PropertyName == nameof(SearchViewModel.Query))
                {
                    OnPropertyChanged(nameof(IsSearching));
                    OnPropertyChanged(nameof(ShowsSections));
                    OnPropertyChanged(nameof(ShowsAppearance));
                }
            };
        }

        foreach (ThemeName theme in new[] { ThemeName.Paper, ThemeName.Instrument, ThemeName.NightDesk })
        {
            Themes.Add(new ThemeChoiceViewModel(theme));
        }

        if (main.ThemeToggle is { } appearance)
        {
            appearance.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ThemeToggleViewModel.Theme))
                {
                    MarkCurrentTheme();
                }
            };
        }

        MarkCurrentTheme();
        ShowSwatches(dark: true);
    }

    public MainWindowViewModel Main { get; }

    public SearchViewModel? Search => Main.Search;

    public SyncLightViewModel? Sync => Main.SyncLight;

    public bool HasSync => Sync is not null;

    public ThemeToggleViewModel? Appearance => Main.ThemeToggle;

    public bool HasAppearance => Appearance is not null;

    /// <summary>The themes, Paper · Instrument · Night Desk (the board's order).</summary>
    public ObservableCollection<ThemeChoiceViewModel> Themes { get; } = [];

    /// <summary>A search is typed: its results take the place of Sync and Appearance until it is cleared.</summary>
    public bool IsSearching => Search?.HasQuery == true;

    public bool ShowsSections => !IsSearching;

    public bool ShowsAppearance => HasAppearance && !IsSearching;

    /// <summary>The status row's device list is unfolded (remembered on this PC).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsDevices))]
    public partial bool IsDevicesOpen { get; set; }

    /// <summary>The device list shows: unfolded, and there is a sync light to read.</summary>
    public bool ShowsDevices => IsDevicesOpen && HasSync;

    /// <summary>The Theme row's list is unfolded (remembered on this PC).</summary>
    [ObservableProperty]
    public partial bool IsThemesOpen { get; set; }

    /// <summary>A sync runs now: "Syncing…" with the icon turning.</summary>
    public bool IsSyncing => Main.IsRefreshing;

    /// <summary>"Sync now", "Syncing…" while it runs, then "Synced just now" for a minute (9.17).</summary>
    public string SyncNowText => IsSyncing ? "Syncing…" : _syncedJustNow ? "Synced just now" : "Sync now";

    /// <summary>The button's tip: what it holds, and the sync light's own tip while the dot shows ("Synced · Pixel 8", a sync problem).</summary>
    public string ButtonTip => Sync is { IsVisible: true } light && light.TipText.Length > 0
        ? $"{AccessibleName} (Ctrl+F)\n{light.TipText}"
        : $"{AccessibleName} (Ctrl+F)";

    /// <summary>Asked to close (a choice that leaves for another page or window).</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Light or dark is shown now (from the window's actual theme, Windows' own while on System): the swatches follow it.</summary>
    public void ShowVariant(bool dark)
    {
        if (Appearance is { } appearance)
        {
            appearance.IsDarkShown = dark;
        }

        ShowSwatches(dark);
    }

    [RelayCommand]
    private void ToggleDevices()
    {
        IsDevicesOpen = !IsDevicesOpen;
        _ = _folds?.SetFoldsAsync(IsDevicesOpen, IsThemesOpen);
    }

    [RelayCommand]
    private void ToggleThemes()
    {
        IsThemesOpen = !IsThemesOpen;
        _ = _folds?.SetFoldsAsync(IsDevicesOpen, IsThemesOpen);
    }

    /// <summary>Sync now (also Ctrl+R anywhere): the window's refresh; nothing while one runs.</summary>
    [RelayCommand]
    private void SyncNow()
    {
        if (Main.RefreshNowCommand.CanExecute(null))
        {
            Main.RefreshNowCommand.Execute(null);
        }
    }

    [RelayCommand]
    private void ShowQrCodes()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
        Main.ShowSyncQrCodesCommand.Execute(null);
    }

    [RelayCommand]
    private void OpenDevicesSettings()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
        Main.OpenDevicesAndSyncCommand.Execute(null);
    }

    [RelayCommand]
    private void OpenAppearanceSettings()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
        Appearance?.OpenSettingsCommand.Execute(null);
    }

    /// <summary>Light, Dark or System: applies at once, the menu stays open (9.24).</summary>
    [RelayCommand]
    private void SetMode(LightDarkMode mode)
    {
        Appearance?.SetModeCommand.Execute(mode);
    }

    [RelayCommand]
    private void PickTheme(ThemeChoiceViewModel? choice)
    {
        if (choice is not null)
        {
            Appearance?.SetThemeCommand.Execute(choice.Theme);
        }
    }

    private void OnMainChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.IsRefreshing))
        {
            return;
        }

        bool refreshing = Main.IsRefreshing;
        if (_wasRefreshing && !refreshing)
        {
            _syncedJustNow = true;
            _syncedTimer.Stop();
            _syncedTimer.Start();
        }

        _wasRefreshing = refreshing;
        OnPropertyChanged(nameof(IsSyncing));
        OnPropertyChanged(nameof(SyncNowText));
    }

    private void MarkCurrentTheme()
    {
        ThemeName current = Appearance?.Theme ?? ThemeName.Instrument;
        foreach (ThemeChoiceViewModel choice in Themes)
        {
            choice.IsCurrent = choice.Theme == current;
        }
    }

    private void ShowSwatches(bool dark)
    {
        foreach (ThemeChoiceViewModel choice in Themes)
        {
            choice.ShowSwatch(dark);
        }
    }
}
