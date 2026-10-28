using System.Collections.ObjectModel;
using System.Globalization;
using AccountabiliBuddy.App.Services.Theming;
using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Settings → On screen &amp; sound → Appearance (and the migration assistant's page): theme, light or dark, the set times or
/// place for After sunset, and how much colour. Every change applies and saves at once; none of it is behind the session lock.
/// </summary>
public sealed partial class AppearanceViewModel : ObservableObject, IDisposable
{
    // The order the mode buttons are shown in.
    private static readonly LightDarkMode[] ModeOrder =
        [LightDarkMode.Light, LightDarkMode.Dark, LightDarkMode.System, LightDarkMode.SetTime, LightDarkMode.Sunset];

    private readonly AppTheme _appearance;
    private readonly CityIndex _cities = new();
    private City? _selectedCity;
    private int _cityQueryVersion;

    public AppearanceViewModel(AppTheme appearance)
    {
        _appearance = appearance;
        _appearance.Changed += OnAppearanceChanged;
        LatitudeText = string.Empty;
        LongitudeText = string.Empty;
        CityQuery = string.Empty;
        TypedMessage = string.Empty;
        ShowTypedPlace();
    }

    /// <summary>In the order of <see cref="ThemeName"/>.</summary>
    public static IReadOnlyList<string> ThemeNames { get; } = ["Instrument", "Night Desk", "Paper"];

    public static IReadOnlyList<string> ModeNames { get; } = ["Light", "Dark", "System", "At a set time", "After sunset"];

    /// <summary>In the order of <see cref="LocationSource"/>.</summary>
    public static IReadOnlyList<string> SourceNames { get; } = ["Windows location", "Type it", "Find a city"];

    /// <summary>In the order of <see cref="ColourMode"/>.</summary>
    public static IReadOnlyList<string> ColourNames { get; } = ["Quiet", "Standard"];

    public ObservableCollection<City> CityResults { get; } = [];

    public int ThemeIndex
    {
        get => (int)_appearance.Theme;
        set
        {
            if (value >= 0 && value != (int)_appearance.Theme)
            {
                _ = _appearance.SetThemeAsync((ThemeName)value);
            }
        }
    }

    /// <summary>Keeps the theme shown as the user's pick (the migration assistant's Finish), even when it is the default one.</summary>
    public void ConfirmTheme()
    {
        if (!_appearance.HasPickedTheme)
        {
            _ = _appearance.SetThemeAsync(_appearance.Theme);
        }
    }

    public int ModeIndex
    {
        get => Array.IndexOf(ModeOrder, _appearance.Mode);
        set
        {
            if (value >= 0 && value < ModeOrder.Length && ModeOrder[value] != _appearance.Mode)
            {
                _ = _appearance.SetModeAsync(ModeOrder[value]);
            }
        }
    }

    public bool IsSetTimeMode => _appearance.Mode == LightDarkMode.SetTime;

    public bool IsSunsetMode => _appearance.Mode == LightDarkMode.Sunset;

    /// <summary>The set times also stand in for sunset until a place is known.</summary>
    public bool ShowsSetTimes => IsSetTimeMode || IsSunsetMode;

    public TimeSpan? DarkAtTime
    {
        get => _appearance.DarkAt.ToTimeSpan();
        set
        {
            if (value is TimeSpan time && TimeOnly.FromTimeSpan(time) != _appearance.DarkAt)
            {
                _ = _appearance.SetTimesAsync(TimeOnly.FromTimeSpan(time), _appearance.LightAt);
            }
        }
    }

    public TimeSpan? LightAtTime
    {
        get => _appearance.LightAt.ToTimeSpan();
        set
        {
            if (value is TimeSpan time && TimeOnly.FromTimeSpan(time) != _appearance.LightAt)
            {
                _ = _appearance.SetTimesAsync(_appearance.DarkAt, TimeOnly.FromTimeSpan(time));
            }
        }
    }

    public int SourceIndex
    {
        get => (int)_appearance.Source;
        set
        {
            if (value >= 0 && value != (int)_appearance.Source)
            {
                ReleaseCities();
                _ = _appearance.SetSourceAsync((LocationSource)value);
            }
        }
    }

    public bool IsWindowsSource => _appearance.Source == LocationSource.Windows;

    public bool IsTypedSource => _appearance.Source == LocationSource.Typed;

    public bool IsCitySource => _appearance.Source == LocationSource.City;

    public int ColourIndex
    {
        get => (int)_appearance.Colour;
        set
        {
            if (value >= 0 && value != (int)_appearance.Colour)
            {
                _ = _appearance.SetColourAsync((ColourMode)value);
            }
        }
    }

    public string Summary => _appearance.Summary;

    public string? LocationNote => _appearance.LocationNote;

    public bool HasLocationNote => LocationNote is not null;

    public bool HasPlace => _appearance.Place is not null;

    /// <summary>E.g. "From Windows (51.50, -0.12)". Shown only on this screen.</summary>
    public string PlaceText => _appearance.Place is GeoPoint place
        ? string.Create(CultureInfo.InvariantCulture, $"{_appearance.PlaceLabel} ({place.Latitude:0.00}, {place.Longitude:0.00})")
        : "No place stored.";

    [ObservableProperty]
    public partial string LatitudeText { get; set; }

    [ObservableProperty]
    public partial string LongitudeText { get; set; }

    [ObservableProperty]
    public partial string TypedMessage { get; set; }

    [ObservableProperty]
    public partial string CityQuery { get; set; }

    public bool HasCityResults => CityResults.Count > 0;

    public City? SelectedCity
    {
        get => _selectedCity;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedCity))
            {
                return;
            }

            _ = _appearance.SetCityAsync(value);
            CityQuery = string.Empty;
            ReleaseCities();
        }
    }

    // The list stays loaded while the picker is open (even with the box cleared) and goes in ReleaseCities.
    partial void OnCityQueryChanged(string value) => _ = ShowCityResultsAsync(value);

    private async Task ShowCityResultsAsync(string query)
    {
        int version = ++_cityQueryVersion;
        IReadOnlyList<City> found = await _cities.SearchAsync(query);
        if (version != _cityQueryVersion)
        {
            return; // a newer keystroke, or the picker closed, while this one loaded
        }

        CityResults.Clear();
        foreach (City city in found)
        {
            CityResults.Add(city);
        }

        OnPropertyChanged(nameof(HasCityResults));
    }

    [RelayCommand]
    private async Task UseTypedPlaceAsync()
    {
        if (!TryParse(LatitudeText, out double latitude) || !TryParse(LongitudeText, out double longitude)
            || !await _appearance.SetTypedPlaceAsync(new GeoPoint(latitude, longitude)))
        {
            TypedMessage = "Latitude is a number from -90 to 90 and longitude from -180 to 180, e.g. 51.50 and -0.12.";
            return;
        }

        TypedMessage = string.Empty;
    }

    [RelayCommand]
    private Task ForgetPlaceAsync()
    {
        LatitudeText = string.Empty;
        LongitudeText = string.Empty;
        TypedMessage = string.Empty;
        return _appearance.ForgetPlaceAsync();
    }

    /// <summary>Lets the city list go (the picker closed, or this page left).</summary>
    public void ReleaseCities()
    {
        _cityQueryVersion++;
        CityResults.Clear();
        _selectedCity = null;
        OnPropertyChanged(nameof(HasCityResults));
        OnPropertyChanged(nameof(SelectedCity));
        _cities.Release();
    }

    public void Dispose()
    {
        _appearance.Changed -= OnAppearanceChanged;
        ReleaseCities();
    }

    private void OnAppearanceChanged(object? sender, EventArgs e)
    {
        foreach (string name in new[]
        {
            nameof(ThemeIndex), nameof(ModeIndex), nameof(IsSetTimeMode), nameof(IsSunsetMode), nameof(ShowsSetTimes), nameof(DarkAtTime),
            nameof(LightAtTime), nameof(SourceIndex), nameof(IsWindowsSource), nameof(IsTypedSource), nameof(IsCitySource), nameof(ColourIndex),
            nameof(Summary), nameof(LocationNote), nameof(HasLocationNote), nameof(HasPlace), nameof(PlaceText),
        })
        {
            OnPropertyChanged(name);
        }
    }

    private void ShowTypedPlace()
    {
        if (_appearance.Source == LocationSource.Typed && _appearance.Place is GeoPoint place)
        {
            LatitudeText = place.Latitude.ToString("0.####", CultureInfo.InvariantCulture);
            LongitudeText = place.Longitude.ToString("0.####", CultureInfo.InvariantCulture);
        }
    }

    // Accepts "51.5" and "51,5".
    private static bool TryParse(string text, out double value) =>
        double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
}
