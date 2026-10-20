using System.Text.Json;
using AccountabiliBuddy.App.Services.Backup;
using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Link;
using AccountabiliBuddy.Core.Storage;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Services.Link;

/// <summary>
/// The app's side of the link (<see cref="ILinkSessionHost"/>): the running session, the profiles and the count-up
/// profile, starting a session, ending one early by the same rules as the End session dialog, and the settings the
/// device shows (profiles, sleep, day bar, upcoming events; L2). Every call runs on the UI thread, where the session
/// lives.
/// </summary>
public sealed class LinkSessionHost : ILinkSessionHost
{
    private const int MaxMinutes = 12 * 60;

    // The documents a backup also carries for the phone (see BackupService.SettingsKeys).
    private static readonly string[] SettingsKeys = { "sleep", "daybar" };

    // Events from yesterday to this many days ahead, as the PC's day bar shows them (visible calendars).
    private const int EventDaysAhead = 14;

    private readonly SessionController _sessions;
    private readonly ConfigurationService _configuration;
    private readonly QuickSessionService _quickSession;
    private readonly SqliteJsonDocumentStore _documents;
    private readonly CalendarService _calendars;

    public LinkSessionHost(
        SessionController sessions,
        ConfigurationService configuration,
        QuickSessionService quickSession,
        SqliteJsonDocumentStore documents,
        CalendarService calendars,
        SleepSettingsService sleep,
        DayBarSettingsService dayBar)
    {
        _sessions = sessions;
        _configuration = configuration;
        _quickSession = quickSession;
        _documents = documents;
        _calendars = calendars;

        _sessions.StateChanged += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
        _configuration.Changed += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
        _quickSession.CountUpProfileChanged += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);

        // The link sends them only when they really changed (it compares a hash), so raising often is fine.
        _configuration.Changed += (_, _) => SettingsChanged?.Invoke(this, EventArgs.Empty);
        sleep.Changed += (_, _) => SettingsChanged?.Invoke(this, EventArgs.Empty);
        dayBar.Changed += (_, _) => SettingsChanged?.Invoke(this, EventArgs.Empty);
        calendars.Changed += (_, _) => SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public string PcName => Environment.MachineName;

    public DateTimeOffset Now => _sessions.Now;

    public event EventHandler? StateChanged;

    public event EventHandler? SettingsChanged;

    /// <summary>Raised (on the UI thread) with the reason when a linked device ended the session early.</summary>
    public event EventHandler<string>? EndedFromDevice;

    public Task<LinkState> GetStateAsync()
    {
        return Dispatcher.UIThread.InvokeAsync(BuildState).GetTask();
    }

    public Task<JsonElement> GetSettingsAsync()
    {
        return Dispatcher.UIThread.InvokeAsync(BuildSettingsAsync);
    }

    /// <summary>A backup's shape, so the phone reads it as it reads backups; nothing in it changes unless the data did.</summary>
    private async Task<JsonElement> BuildSettingsAsync()
    {
        string profiles = AccountabiliBuddy.Core.Storage.ProfileDocumentSerializer.Serialize(_configuration.Current);
        var settings = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (string key in SettingsKeys)
        {
            if (await _documents.LoadAsync<JsonElement?>(key) is JsonElement value)
            {
                settings[key] = value;
            }
        }

        DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_sessions.Now, _sessions.TimeProvider.LocalTimeZone).DateTime);
        var events = new List<CalendarEvent>();
        var seen = new HashSet<(string, string)>();
        for (int day = -1; day <= EventDaysAhead; day++)
        {
            foreach (CalendarEntry entry in _calendars.EventsOn(today.AddDays(day)))
            {
                if (seen.Add((entry.Event.CalendarId, entry.Event.Id)))
                {
                    events.Add(entry.Event);
                }
            }
        }

        var content = new { profiles, settings, calendarEvents = events, appVersion = BackupService.AppVersion };
        return JsonSerializer.SerializeToElement(content, LinkFraming.JsonOptions);
    }

    public Task<string?> StartAsync(LinkStartRequest request)
    {
        return Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (_sessions.State.IsActive)
            {
                return "A session is already running on the PC.";
            }

            if (request.CountUp)
            {
                return await _quickSession.StartCountUpAsync() ? null : "The Quick Start profile couldn't be set up on the PC.";
            }

            if (request.ProfileId is not Guid profileId || _configuration.Current.Profiles.All(profile => profile.Id != profileId))
            {
                return "That profile isn't on the PC any more.";
            }

            if (request.Minutes is not int minutes || minutes < 1 || minutes > MaxMinutes)
            {
                return "Pick a length between 1 minute and 12 hours.";
            }

            return _sessions.StartManual(profileId, TimeSpan.FromMinutes(minutes)) ? (string?)null : "The session couldn't start.";
        });
    }

    public Task<LinkEndEarlyTerms?> GetEndEarlyTermsAsync()
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            SessionState state = _sessions.State;
            if (!state.IsActive)
            {
                return (LinkEndEarlyTerms?)null;
            }

            DateTimeOffset now = _sessions.Now;
            if (!state.IsEnforcing)
            {
                return new LinkEndEarlyTerms(0, 0, now, "On a break: it ends right away.");
            }

            BypassSettings bypass = _sessions.BypassSettings;
            string? note = _sessions.EndEarlyWaitMatchesUnlock
                ? "A scheduled block: the wait matches the profile's shortest unlock option."
                : null;
            return new LinkEndEarlyTerms((int)Math.Ceiling(bypass.WaitDuration.TotalSeconds), bypass.MinReasonLength, now + bypass.WaitDuration, note);
        }).GetTask();
    }

    public Task<string?> EndEarlyAsync(string reason)
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!_sessions.State.IsActive)
            {
                return (string?)"No session is running.";
            }

            bool wasFocus = _sessions.IsEnforcing;
            _sessions.EndSession(wasFocus ? reason : null);
            if (wasFocus)
            {
                EndedFromDevice?.Invoke(this, reason);
            }

            return (string?)null;
        }).GetTask();
    }

    private LinkState BuildState()
    {
        SessionState state = _sessions.State;
        IReadOnlyList<FocusProfile> profiles = _configuration.Current.Profiles;
        string? mode = state.Mode switch
        {
            SessionMode.Manual => "manual",
            SessionMode.Pomodoro => "pomodoro",
            SessionMode.Scheduled => "scheduled",
            _ => null,
        };
        string phase = state.Phase switch
        {
            SessionPhase.Focus => "focus",
            SessionPhase.ShortBreak => "shortBreak",
            SessionPhase.LongBreak => "longBreak",
            _ => "idle",
        };

        return new LinkState(
            mode,
            phase,
            CountsAsFocus: state.IsEnforcing && state.Mode != SessionMode.Scheduled,
            state.ProfileId,
            ProfileOptions.NameOf(profiles, state.ProfileId),
            state.PhaseStartedAt,
            state.PhaseEndsAt,
            profiles.Select(profile => new LinkProfile(profile.Id, profile.Name)).ToList(),
            _quickSession.CountUpProfileId,
            _sessions.Now);
    }
}
