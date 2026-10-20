using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The calendars shown in the Exceptions tab and everything kept about them on this computer (documents table):
/// <list type="bullet">
/// <item>"calendar.sources": the in-app calendar, iCal links and Google accounts, each calendar's colour and
/// visibility. Link addresses and Google tokens are DPAPI-encrypted.</item>
/// <item>"calendar.events": events added in the app.</item>
/// <item>"calendar.cache": fetched events and how each fetch went (filled by <see cref="CalendarSyncService"/>).</item>
/// <item>"calendar.settings": check interval and the Google client.</item>
/// </list>
/// Hidden calendars are still kept; they are only left out of <see cref="EventsOn"/>. Events are for planning only
/// and never affect enforcement. Use from the UI thread only.
/// </summary>
public sealed class CalendarService
{
    public const string LocalSourceId = "local";
    public const string LocalCalendarId = "local:events";

    private const string SourcesKey = "calendar.sources";
    private const string LocalEventsKey = "calendar.events";
    private const string CacheKey = "calendar.cache";
    private const string SettingsKey = "calendar.settings";

    /// <summary>Calendar colours, mid-tone so white text stays readable.</summary>
    private static readonly string[] Palette =
    {
        "#2563EB", "#0F766E", "#9333EA", "#DB2777", "#C2410C", "#4D7C0F", "#A16207", "#475569",
    };

    private readonly SqliteJsonDocumentStore _store;

    // A check that only moved "last checked" (nothing new, no new error) saves the cache at most this often: the cache
    // holds every fetched event, and Google is checked every 30 seconds.
    private static readonly TimeSpan CheckTimesSaveInterval = TimeSpan.FromMinutes(10);

    // Saves run one after another so an older state never overwrites a newer one.
    private Task _pendingSave = Task.CompletedTask;

    // The check time of the last status that was saved (by the fetch's own clock).
    private DateTimeOffset? _checkTimesSavedAt;

    public CalendarService(SqliteJsonDocumentStore store)
    {
        _store = store;
    }

    public IReadOnlyList<CalendarSource> Sources { get; private set; } = new[] { CreateLocalSource() };

    public IReadOnlyList<CalendarEvent> LocalEvents { get; private set; } = Array.Empty<CalendarEvent>();

    public CalendarCache Cache { get; private set; } = CalendarCache.Empty;

    public CalendarSettings Settings { get; private set; } = CalendarSettings.Default;

    /// <summary>Raised when sources, calendars, events, statuses or settings change.</summary>
    public event EventHandler? Changed;

    public void LoadBlocking()
    {
        try
        {
            List<CalendarSource>? sources = Load<List<CalendarSource>>(SourcesKey);
            if (sources is { Count: > 0 } && sources.Any(source => source.Kind == CalendarSourceKind.Local))
            {
                Sources = sources;
            }

            LocalEvents = Load<List<CalendarEvent>>(LocalEventsKey) ?? (IReadOnlyList<CalendarEvent>)Array.Empty<CalendarEvent>();
            Cache = Load<CalendarCache>(CacheKey) ?? CalendarCache.Empty;
            Settings = Load<CalendarSettings>(SettingsKey) ?? CalendarSettings.Default;
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            // Unreadable calendar data: start with the empty in-app calendar rather than failing to launch.
        }
    }

    /// <summary>Events of visible calendars on <paramref name="date"/>, all-day first, then by start time.</summary>
    public IReadOnlyList<CalendarEntry> EventsOn(DateOnly date)
    {
        var entries = new List<CalendarEntry>();
        foreach (CalendarSource source in Sources)
        {
            foreach (CalendarInfo calendar in source.Calendars.Where(calendar => calendar.IsVisible))
            {
                entries.AddRange(EventsOf(calendar.Id)
                    .Where(calendarEvent => calendarEvent.OccursOn(date))
                    .Select(calendarEvent => new CalendarEntry(calendarEvent, calendar, source)));
            }
        }

        return entries
            .OrderByDescending(entry => entry.Event.IsAllDay)
            .ThenBy(entry => entry.Event.LocalStart)
            .ToList();
    }

    /// <summary>Every kept event of a calendar, visible or not.</summary>
    public IReadOnlyList<CalendarEvent> EventsOf(string calendarId)
    {
        if (calendarId == LocalCalendarId)
        {
            return LocalEvents;
        }

        return Cache.EventsByCalendar.TryGetValue(calendarId, out IReadOnlyList<CalendarEvent>? events)
            ? events
            : Array.Empty<CalendarEvent>();
    }

    public CalendarSourceStatus StatusOf(string sourceId)
    {
        return Cache.StatusBySource.TryGetValue(sourceId, out CalendarSourceStatus? status) ? status : new CalendarSourceStatus();
    }

    public void AddLocalEvent(string title, DateTime localStart, DateTime localEnd, bool isAllDay)
    {
        var calendarEvent = new CalendarEvent(Guid.NewGuid().ToString("N"), LocalCalendarId, title, localStart, localEnd, isAllDay);
        LocalEvents = LocalEvents.Append(calendarEvent).OrderBy(item => item.LocalStart).ToList();
        Save(LocalEventsKey, LocalEvents);
    }

    public void UpdateLocalEvent(string eventId, string title, DateTime localStart, DateTime localEnd, bool isAllDay)
    {
        LocalEvents = LocalEvents
            .Select(item => item.Id == eventId
                ? item with { Title = title, LocalStart = localStart, LocalEnd = localEnd, IsAllDay = isAllDay }
                : item)
            .OrderBy(item => item.LocalStart)
            .ToList();
        Save(LocalEventsKey, LocalEvents);
    }

    public void RemoveLocalEvent(string eventId)
    {
        LocalEvents = LocalEvents.Where(item => item.Id != eventId).ToList();
        Save(LocalEventsKey, LocalEvents);
    }

    public void SetVisible(string calendarId, bool isVisible)
    {
        UpdateCalendar(calendarId, calendar => calendar with { IsVisible = isVisible });
    }

    /// <summary>Gives the calendar the next colour of the palette.</summary>
    public void NextColor(string calendarId)
    {
        UpdateCalendar(calendarId, calendar =>
        {
            int index = Array.IndexOf(Palette, calendar.Color.ToUpperInvariant());
            return calendar with { Color = Palette[(index + 1) % Palette.Length] };
        });
    }

    /// <summary>A palette colour not yet used by any calendar, if there is one.</summary>
    public string NextUnusedColor()
    {
        var used = new HashSet<string>(Sources.SelectMany(source => source.Calendars).Select(calendar => calendar.Color.ToUpperInvariant()));
        return Palette.FirstOrDefault(color => !used.Contains(color)) ?? Palette[used.Count % Palette.Length];
    }

    /// <summary>Adds a source, or replaces the one with the same id (keeping each calendar's colour and visibility).</summary>
    public void SaveSource(CalendarSource source)
    {
        CalendarSource? existing = Sources.FirstOrDefault(candidate => candidate.Id == source.Id);
        if (existing is not null)
        {
            source = source with
            {
                Calendars = source.Calendars
                    .Select(calendar => existing.Calendars.FirstOrDefault(old => old.Id == calendar.Id) is { } old
                        ? calendar with { Color = old.Color, IsVisible = old.IsVisible }
                        : calendar)
                    .ToList(),
            };
        }

        Sources = existing is null
            ? Sources.Append(source).ToList()
            : Sources.Select(candidate => candidate.Id == source.Id ? source : candidate).ToList();

        // Calendars that disappeared from the source take their events with them.
        if (existing is not null)
        {
            IEnumerable<string> gone = existing.Calendars.Select(calendar => calendar.Id)
                .Except(source.Calendars.Select(calendar => calendar.Id));
            Cache = Cache with { EventsByCalendar = Without(Cache.EventsByCalendar, gone) };
            Save(CacheKey, Cache);
        }

        Save(SourcesKey, Sources);
    }

    public void RemoveSource(string sourceId)
    {
        CalendarSource? source = Sources.FirstOrDefault(candidate => candidate.Id == sourceId);
        if (source is null || source.Kind == CalendarSourceKind.Local)
        {
            return;
        }

        Sources = Sources.Where(candidate => candidate.Id != sourceId).ToList();
        Cache = new CalendarCache(
            Without(Cache.EventsByCalendar, source.Calendars.Select(calendar => calendar.Id)),
            Without(Cache.StatusBySource, new[] { sourceId }));
        Save(CacheKey, Cache);
        Save(SourcesKey, Sources);
    }

    /// <summary>Replaces all kept events of a calendar (a full fetch).</summary>
    public void SetEvents(string calendarId, IReadOnlyList<CalendarEvent> events)
    {
        var byCalendar = new Dictionary<string, IReadOnlyList<CalendarEvent>>(Cache.EventsByCalendar) { [calendarId] = events };
        Cache = Cache with { EventsByCalendar = byCalendar };
        Save(CacheKey, Cache);
    }

    /// <summary>Applies changed and cancelled events to a calendar (an incremental fetch).</summary>
    public void ApplyEventChanges(string calendarId, IReadOnlyList<CalendarEvent> changed, IReadOnlyCollection<string> cancelledIds)
    {
        if (changed.Count == 0 && cancelledIds.Count == 0)
        {
            return;
        }

        var removed = new HashSet<string>(cancelledIds.Concat(changed.Select(item => item.Id)));
        List<CalendarEvent> events = EventsOf(calendarId)
            .Where(item => !removed.Contains(item.Id))
            .Concat(changed)
            .OrderBy(item => item.LocalStart)
            .ToList();
        SetEvents(calendarId, events);
    }

    public void SetStatus(string sourceId, CalendarSourceStatus status)
    {
        CalendarSourceStatus previous = StatusOf(sourceId);
        var bySource = new Dictionary<string, CalendarSourceStatus>(Cache.StatusBySource) { [sourceId] = status };
        Cache = Cache with { StatusBySource = bySource };

        // Only the check times moved: shown at once, written now and then. After a restart an older "last checked"
        // only means the first check asks for a few more changes.
        bool onlyCheckTimesMoved = status with { LastAttemptAt = previous.LastAttemptAt, LastSuccessAt = previous.LastSuccessAt } == previous;
        DateTimeOffset checkedAt = status.LastAttemptAt ?? DateTimeOffset.MinValue;
        if (onlyCheckTimesMoved && _checkTimesSavedAt is DateTimeOffset savedAt && checkedAt - savedAt < CheckTimesSaveInterval)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        _checkTimesSavedAt = checkedAt;
        Save(CacheKey, Cache);
    }

    public void SaveSettings(CalendarSettings settings)
    {
        Settings = settings with { LinkRefreshMinutes = Math.Max(CalendarSettings.MinLinkRefreshMinutes, settings.LinkRefreshMinutes) };
        Save(SettingsKey, Settings);
    }

    private T? Load<T>(string key)
    {
        return Task.Run(() => _store.LoadAsync<T>(key)).GetAwaiter().GetResult();
    }

    private void UpdateCalendar(string calendarId, Func<CalendarInfo, CalendarInfo> change)
    {
        Sources = Sources
            .Select(source => source with
            {
                Calendars = source.Calendars.Select(calendar => calendar.Id == calendarId ? change(calendar) : calendar).ToList(),
            })
            .ToList();
        Save(SourcesKey, Sources);
    }

    private void Save<T>(string key, T value)
    {
        Changed?.Invoke(this, EventArgs.Empty);
        _pendingSave = _pendingSave.ContinueWith(
            async _ =>
            {
                try
                {
                    await _store.SaveAsync(key, value);
                }
                catch (SqliteException)
                {
                    // Best effort; the in-memory state still applies until the app closes.
                }
            },
            TaskScheduler.Default).Unwrap();
    }

    private static Dictionary<string, TValue> Without<TValue>(IReadOnlyDictionary<string, TValue> source, IEnumerable<string> keys)
    {
        var result = new Dictionary<string, TValue>(source);
        foreach (string key in keys)
        {
            result.Remove(key);
        }

        return result;
    }

    private static CalendarSource CreateLocalSource()
    {
        return new CalendarSource(
            LocalSourceId,
            CalendarSourceKind.Local,
            "On this computer",
            new[] { new CalendarInfo(LocalCalendarId, "My events", Palette[0]) });
    }
}
