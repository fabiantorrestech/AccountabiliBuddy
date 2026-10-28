using System.Text.Json;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Platform.Windows.Security;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Theming;

/// <summary>
/// The app's look (ADR 49): which theme, light or dark (always, as Windows, at set times or after sunset) and how much
/// colour. Stored in the app-owned document "theme", which began as <c>{"Choice":n}</c>. It swaps the theme dictionary
/// through <see cref="ThemeService"/> and sets the light or dark variant itself. Cosmetic, so it saves at once and sits
/// outside the session lock. Use from the UI thread.
/// </summary>
/// <remarks>
/// Switching uses the app's clock (<see cref="TimeProvider"/>) and one timer set for the next switch moment (no polling);
/// it is planned again after the timer fires, when a setting changes, when Windows wakes from sleep and when the clock
/// jumps. The place for After sunset is private: stored DPAPI-encrypted, never synced or logged. Windows location is read at
/// most once a day and only while After sunset uses it.
/// </remarks>
public sealed class AppTheme : IDisposable
{
    private const string DocumentKey = "theme";

    // A backstop: even when no switch is near, the plan is looked at again this often (time zone and daylight-saving changes
    // raise no event the app hears).
    private static readonly TimeSpan LongestWait = TimeSpan.FromHours(3);
    private static readonly TimeSpan Margin = TimeSpan.FromSeconds(1);

    private readonly SqliteJsonDocumentStore _store;
    private readonly ThemeService _themes;
    private readonly TimeProvider _time;
    private readonly IDataProtector _protector;
    private readonly ILocationProvider _location;

    private ITimer? _timer;
    private DarkPlan? _plan;
    private bool _isDark;
    private bool _readingLocation;
    private bool _disposed;
    private DateOnly? _askedOn;
    private int? _cityId;
    private string? _placeLabel;

    public AppTheme(SqliteJsonDocumentStore store, ThemeService themes, TimeProvider time, IDataProtector protector, ILocationProvider location)
    {
        _store = store;
        _themes = themes;
        _time = time;
        _protector = protector;
        _location = location;
        DarkAt = new TimeOnly(19, 0);
        LightAt = new TimeOnly(7, 0);
        Summary = string.Empty;

        // The Windows clock was set far from real time (or back): the next switch moment moved.
        if (time is TrustedClock trusted)
        {
            trusted.SystemClockOffsetChanged += OnClockChanged;
        }

        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Microsoft.Win32.SystemEvents.TimeChanged += OnTimeChanged;
    }

    /// <summary>Raised on the UI thread when anything shown in Settings changed, including the summary line.</summary>
    public event EventHandler? Changed;

    public ThemeName Theme { get; private set; } = ThemeName.Instrument;

    /// <summary>False until the user picks a theme (the stored document has no Theme yet); Instrument is used meanwhile.</summary>
    public bool HasPickedTheme { get; private set; }

    public LightDarkMode Mode { get; private set; }

    public TimeOnly DarkAt { get; private set; }

    public TimeOnly LightAt { get; private set; }

    public LocationSource Source { get; private set; }

    public ColourMode Colour { get; private set; }

    /// <summary>Quiet colour: colour only for progress and the one main button. Views read this (and the resource Ab.IsQuietColour).</summary>
    public bool IsQuietColour => Colour == ColourMode.Quiet;

    /// <summary>The stored place for After sunset, or null. Private: show it only in Settings → Appearance.</summary>
    public GeoPoint? Place { get; private set; }

    /// <summary>What to show for the stored place, e.g. "Berlin, BE, DE", "From Windows" or the typed numbers' label.</summary>
    public string? PlaceLabel => Place is null ? null : _placeLabel;

    /// <summary>The line under the Appearance card, e.g. "Instrument by day, dark at 6:12 PM tonight."</summary>
    public string Summary { get; private set; }

    /// <summary>Why After sunset is using the set times or an old place, or null when all is well.</summary>
    public string? LocationNote
    {
        get
        {
            if (Mode != LightDarkMode.Sunset)
            {
                return null;
            }

            if (Source == LocationSource.Windows)
            {
                return _location.Access switch
                {
                    LocationAccess.Denied => Place is null
                        ? "Windows location is off for this app, so the set times are used. Turn it on in Windows Settings, Privacy & security, Location (including desktop apps), or type a place or pick a city."
                        : "Windows location is off for this app, so the last place it gave is used.",
                    LocationAccess.Unavailable => Place is null
                        ? "Windows couldn't work out a location, so the set times are used. It tries again tomorrow."
                        : "Windows couldn't work out a location today, so the last place it gave is used.",
                    _ when Place is not null => null,
                    _ when _askedOn == Today() => "Windows location is read once a day and today's read is used up, so the set times are used. It is read again tomorrow.",
                    _ => "Waiting for Windows location; the set times are used until it answers.",
                };
            }

            return Place is null
                ? Source == LocationSource.Typed ? "Type a latitude and longitude; the set times are used until then." : "Pick a city; the set times are used until then."
                : null;
        }
    }

    /// <summary>True when the app should look dark right now (only meaningful for the set times and sunset).</summary>
    public bool IsDarkNow => _isDark;

    public void LoadBlocking()
    {
        StoredAppearance stored;
        try
        {
            stored = Task.Run(() => _store.LoadAsync<StoredAppearance>(DocumentKey)).GetAwaiter().GetResult() ?? new StoredAppearance();
        }
        catch (Exception ex) when (ex is SqliteException or JsonException)
        {
            stored = new StoredAppearance();
        }

        Mode = Enum.IsDefined((LightDarkMode)stored.Choice) ? (LightDarkMode)stored.Choice : LightDarkMode.System;
        HasPickedTheme = Enum.TryParse(stored.Theme, out ThemeName theme) && Enum.IsDefined(theme);
        Theme = HasPickedTheme ? theme : ThemeName.Instrument;
        DarkAt = stored.DarkAt;
        LightAt = stored.LightAt;
        Source = Enum.IsDefined((LocationSource)stored.LocationSource) ? (LocationSource)stored.LocationSource : LocationSource.Windows;
        Colour = Enum.IsDefined((ColourMode)stored.Colour) ? (ColourMode)stored.Colour : ColourMode.Quiet;
        _askedOn = stored.LocationAskedOn;
        ReadPlace(stored.ProtectedLocation);

        ApplyAndPlan();
        // Once the app is running: the once-a-day read of Windows location, if After sunset needs it.
        Dispatcher.UIThread.Post(() => _ = ReadWindowsLocationIfDueAsync());
    }

    public Task SetThemeAsync(ThemeName theme)
    {
        Theme = theme;
        HasPickedTheme = true;
        return CommitAsync();
    }

    public async Task SetModeAsync(LightDarkMode mode)
    {
        Mode = mode;
        await CommitAsync();
        await ReadWindowsLocationIfDueAsync();
    }

    public Task SetColourAsync(ColourMode colour)
    {
        Colour = colour;
        return CommitAsync();
    }

    public Task SetTimesAsync(TimeOnly darkAt, TimeOnly lightAt)
    {
        DarkAt = darkAt;
        LightAt = lightAt;
        return CommitAsync();
    }

    /// <summary>
    /// Another place source. The stored place stays in use until a new one is set or it is forgotten; Windows location is
    /// still read at most once a day, so choosing it again on a day it was already asked for waits until tomorrow.
    /// </summary>
    public async Task SetSourceAsync(LocationSource source)
    {
        if (source == Source)
        {
            return;
        }

        Source = source;
        await CommitAsync();
        await ReadWindowsLocationIfDueAsync();
    }

    /// <returns>False when the numbers are not a place on Earth.</returns>
    public async Task<bool> SetTypedPlaceAsync(GeoPoint place)
    {
        if (!place.IsValid)
        {
            return false;
        }

        Source = LocationSource.Typed;
        SetPlace(place, null, "Typed in");
        await CommitAsync();
        return true;
    }

    public Task SetCityAsync(City city)
    {
        Source = LocationSource.City;
        SetPlace(city.Point, city.Id, city.Display);
        return CommitAsync();
    }

    /// <summary>Clears the stored place. Windows location is not asked again until tomorrow.</summary>
    public Task ForgetPlaceAsync()
    {
        ClearPlace();
        _askedOn = Today();
        return CommitAsync();
    }

    /// <summary>Works the plan out again now (after waking from sleep, a clock change or a settings change).</summary>
    public void Refresh()
    {
        ApplyAndPlan();
        _ = ReadWindowsLocationIfDueAsync();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_time is TrustedClock trusted)
        {
            trusted.SystemClockOffsetChanged -= OnClockChanged;
        }

        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        Microsoft.Win32.SystemEvents.TimeChanged -= OnTimeChanged;
        _timer?.Dispose();
        _timer = null;
    }

    private async Task CommitAsync()
    {
        ApplyAndPlan();
        await SaveAsync();
    }

    // Theme dictionary, variant, the one timer and the summary line, from the current settings.
    private void ApplyAndPlan()
    {
        if (_disposed)
        {
            return;
        }

        if (_themes.Current != Theme)
        {
            _themes.Apply(Theme);
        }

        _timer?.Dispose();
        _timer = null;
        DateTimeOffset now = _time.GetLocalNow();
        _plan = null;
        if (Mode is LightDarkMode.SetTime or LightDarkMode.Sunset)
        {
            _plan = Mode == LightDarkMode.Sunset && Place is GeoPoint place
                ? DarkSchedule.ForSunset(now, place, DarkAt, LightAt, _time.LocalTimeZone)
                : DarkSchedule.ForSetTimes(now, DarkAt, LightAt, _time.LocalTimeZone);
            _isDark = _plan.IsDark;

            // One timer, to the next switch (a second after it, so the plan then sees it as passed).
            TimeSpan wait = _plan.NextSwitch - now + Margin;
            wait = wait > LongestWait ? LongestWait : wait < Margin ? Margin : wait;
            _timer = _time.CreateTimer(_ => Dispatcher.UIThread.Post(Refresh), null, wait, Timeout.InfiniteTimeSpan);
        }

        if (Application.Current is Application app)
        {
            app.RequestedThemeVariant = Mode switch
            {
                LightDarkMode.Light => ThemeVariant.Light,
                LightDarkMode.Dark => ThemeVariant.Dark,
                LightDarkMode.System => ThemeVariant.Default,
                _ => _isDark ? ThemeVariant.Dark : ThemeVariant.Light,
            };
            app.Resources["Ab.IsQuietColour"] = IsQuietColour;
        }

        Summary = AppearanceSummary.Build(Theme, Mode, DarkAt, LightAt, Place is not null, _plan, now);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // At most once a day, and only while After sunset uses Windows location. Counts as the day's one read even if it
    // fails; a failed read keeps the place already stored.
    private async Task ReadWindowsLocationIfDueAsync()
    {
        if (_disposed || _readingLocation || Mode != LightDarkMode.Sunset || Source != LocationSource.Windows)
        {
            return;
        }

        DateOnly today = Today();
        if (_askedOn == today)
        {
            return;
        }

        _readingLocation = true;
        try
        {
            _askedOn = today;
            await SaveAsync();
            GeoPoint? found = await _location.GetApproximateAsync(CancellationToken.None);

            // Turned off, or moved to another source, while Windows was working it out: drop the answer.
            if (found is GeoPoint place && Mode == LightDarkMode.Sunset && Source == LocationSource.Windows)
            {
                SetPlace(place, null, "From Windows");
                await SaveAsync();
            }
        }
        finally
        {
            _readingLocation = false;
        }

        ApplyAndPlan();
    }

    private void SetPlace(GeoPoint place, int? cityId, string label)
    {
        Place = place;
        _cityId = cityId;
        _placeLabel = label;
    }

    private void ClearPlace()
    {
        Place = null;
        _cityId = null;
        _placeLabel = null;
    }

    private void ReadPlace(string? protectedPlace)
    {
        ClearPlace();
        if (protectedPlace is null)
        {
            return;
        }

        try
        {
            string? json = _protector.Unprotect(Convert.FromBase64String(protectedPlace));
            StoredPlace? place = json is null ? null : JsonSerializer.Deserialize<StoredPlace>(json);
            if (place is not null && new GeoPoint(place.Latitude, place.Longitude) is { IsValid: true } point)
            {
                SetPlace(point, place.CityId, place.Label ?? "Saved place");
            }
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            // An unreadable place counts as none.
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            string? protectedPlace = Place is GeoPoint place
                ? Convert.ToBase64String(_protector.Protect(JsonSerializer.Serialize(new StoredPlace(place.Latitude, place.Longitude, _cityId, _placeLabel))))
                : null;
            var stored = new StoredAppearance
            {
                Choice = (int)Mode,
                Theme = HasPickedTheme ? Theme.ToString() : null,
                DarkAt = DarkAt,
                LightAt = LightAt,
                LocationSource = (int)Source,
                ProtectedLocation = protectedPlace,
                LocationAskedOn = _askedOn,
                Colour = (int)Colour,
            };
            await _store.SaveAsync(DocumentKey, stored);
        }
        catch (Exception ex) when (ex is SqliteException or CryptographicFailureException)
        {
            // Applies for now; written with the next change.
        }
    }

    private DateOnly Today() => DateOnly.FromDateTime(_time.GetLocalNow().DateTime);

    private void OnClockChanged(object? sender, TimeSpan offset) => Dispatcher.UIThread.Post(Refresh);

    private void OnTimeChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        // A changed time zone or daylight-saving rule: the zone's cached offsets may be stale.
        TimeZoneInfo.ClearCachedData();
        Refresh();
    });

    private void OnPowerModeChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Resume)
        {
            Dispatcher.UIThread.Post(Refresh);
        }
    }
}
