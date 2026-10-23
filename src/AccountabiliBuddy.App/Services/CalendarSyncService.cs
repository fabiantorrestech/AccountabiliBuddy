using System.Net;
using System.Text.Json;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Ports;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Keeps connected calendars up to date (ADR 9). Every fetch keeps events from <see cref="DaysBack"/> days ago to
/// <see cref="DaysAhead"/> days ahead, all calendars of a source, hidden ones included.
/// <list type="bullet">
/// <item>iCal links: every <see cref="CalendarSettings.LinkRefreshMinutes"/> (5 by default), skipping the download
/// when the server says nothing changed.</item>
/// <item>Google: only changes every <see cref="GoogleChangesInterval"/>, everything every <see cref="GoogleFullInterval"/>.</item>
/// <item>Refresh (button, window opened): everything, now.</item>
/// </list>
/// A failed fetch keeps the last good events and shows why. Use from the UI thread only.
/// </summary>
public sealed class CalendarSyncService : IDisposable
{
    public const int DaysBack = 90;
    public const int DaysAhead = 400;

    public static TimeSpan GoogleChangesInterval { get; } = TimeSpan.FromSeconds(30);

    public static TimeSpan GoogleFullInterval { get; } = TimeSpan.FromHours(6);

    // Changes are asked for since a little before the last success, so clock skew can't drop one.
    private static readonly TimeSpan ChangesOverlap = TimeSpan.FromMinutes(2);

    private readonly CalendarService _calendars;
    private readonly CalendarNetwork _network;
    private readonly GoogleCalendarClient _google;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _timeProvider;
    private DispatcherTimer? _timer;
    private readonly Dictionary<string, (string Token, DateTimeOffset ExpiresAt)> _accessTokens = new(StringComparer.Ordinal);
    private readonly HashSet<string> _fetching = new(StringComparer.Ordinal);

    public CalendarSyncService(
        CalendarService calendars,
        CalendarNetwork network,
        GoogleCalendarClient google,
        IDataProtector protector,
        TimeProvider timeProvider)
    {
        _calendars = calendars;
        _network = network;
        _google = google;
        _protector = protector;
        _timeProvider = timeProvider;
    }

    /// <summary>True while any source is being fetched (for a "Refreshing…" hint).</summary>
    public bool IsFetching => _fetching.Count > 0;

    /// <summary>Starts the 15-second timer that fetches whatever is due, and fetches what is due now.</summary>
    public void Start()
    {
        _timer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _timer.Tick += (_, _) => _ = FetchDueAsync();
        _timer.Start();
        _ = FetchDueAsync();
    }

    public void Dispose()
    {
        _timer?.Stop();
    }

    /// <summary>Fetches everything from every connected source now.</summary>
    public async Task RefreshAllAsync()
    {
        foreach (CalendarSource source in _calendars.Sources.Where(source => source.Kind != CalendarSourceKind.Local).ToList())
        {
            await FetchAsync(source, full: true);
        }
    }

    /// <summary>Adds an iCal link and fetches it.</summary>
    /// <param name="groupName">Shown with the calendar, e.g. "Work account"; the address's host when empty.</param>
    /// <returns>Null when added (even if the first fetch failed; its status says why); otherwise why not.</returns>
    public async Task<string?> AddLinkAsync(string address, string? groupName)
    {
        if (!TryNormalizeLink(address, out Uri? uri, out string? error))
        {
            return error;
        }

        string sourceId = "ical:" + Guid.NewGuid().ToString("N");
        var source = new CalendarSource(
            sourceId,
            CalendarSourceKind.ICalLink,
            string.IsNullOrWhiteSpace(groupName) ? uri.Host : groupName.Trim(),
            new[] { new CalendarInfo(sourceId + "|calendar", "Calendar", _calendars.NextUnusedColor()) },
            Protect(uri.AbsoluteUri));
        _calendars.SaveSource(source);
        await FetchAsync(source, full: true);
        return null;
    }

    /// <summary>Signs in to Google in the browser, then lists the account's calendars and fetches them.</summary>
    /// <returns>Null when connected; otherwise why not.</returns>
    public async Task<string?> ConnectGoogleAsync(Func<Uri, bool> openBrowser, CancellationToken cancellationToken)
    {
        if (!TryGetGoogleClient(out string clientId, out string clientSecret))
        {
            return "Enter your Google client ID and secret in Settings first.";
        }

        try
        {
            GoogleTokens tokens = await _google.SignInAsync(clientId, clientSecret, openBrowser, cancellationToken);
            IReadOnlyList<GoogleCalendarEntry> calendars = await _google.ListCalendarsAsync(tokens.AccessToken, cancellationToken);
            string account = calendars.FirstOrDefault(calendar => calendar.IsPrimary)?.Id ?? "Google account";
            string name = $"Google · {account}";

            // The same account again: keep the one there (its colours and choices) with the new sign-in, not a second copy.
            CalendarSource? existing = _calendars.Sources.FirstOrDefault(candidate =>
                candidate.Kind == CalendarSourceKind.Google && string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
            string sourceId = existing?.Id ?? "google:" + Guid.NewGuid().ToString("N");
            _accessTokens[sourceId] = (tokens.AccessToken, tokens.AccessTokenExpiresAt);
            var source = new CalendarSource(
                sourceId,
                CalendarSourceKind.Google,
                name,
                calendars.Select(calendar => existing?.Calendars.FirstOrDefault(known => known.Id == $"{sourceId}|{calendar.Id}") ?? ToCalendarInfo(sourceId, calendar)).ToList(),
                Protect(tokens.RefreshToken));
            _calendars.SaveSource(source);
            await FetchAsync(source, full: true);
            return existing is null ? null : $"{account} was already connected: its sign-in was renewed, no second copy added.";
        }
        catch (GoogleSignInException ex)
        {
            return ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return $"Couldn't connect: {Describe(ex)}";
        }
    }

    /// <summary>Removes an iCal link, or disconnects a Google account (its token is revoked at Google).</summary>
    public async Task RemoveSourceAsync(string sourceId)
    {
        CalendarSource? source = _calendars.Sources.FirstOrDefault(candidate => candidate.Id == sourceId);
        if (source is null || source.Kind == CalendarSourceKind.Local)
        {
            return;
        }

        _calendars.RemoveSource(sourceId);
        _accessTokens.Remove(sourceId);

        if (source.Kind == CalendarSourceKind.Google && Unprotect(source.ProtectedSecret) is { } refreshToken)
        {
            try
            {
                await _google.RevokeAsync(refreshToken, CancellationToken.None);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
            {
                // Best effort: the token is already deleted here; it can also be revoked in the Google account.
            }
        }
    }

    /// <summary>
    /// Accepts https addresses (webcal:// is read as https://), and plain http only to this computer (for testing).
    /// </summary>
    public static bool TryNormalizeLink(string input, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Uri? uri, out string? error)
    {
        uri = null;
        string text = input.Trim();
        if (text.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase))
        {
            text = "https://" + text["webcal://".Length..];
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? parsed))
        {
            error = "That isn't a web address.";
            return false;
        }

        bool secure = parsed.Scheme == Uri.UriSchemeHttps;
        bool local = parsed.Scheme == Uri.UriSchemeHttp && parsed.IsLoopback;
        if (!secure && !local)
        {
            error = "Calendar links must start with https:// (or webcal://).";
            return false;
        }

        uri = parsed;
        error = null;
        return true;
    }

    /// <summary>Fetches every source whose interval is up (called by the timer every 15 s).</summary>
    public async Task FetchDueAsync()
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        TimeSpan linkInterval = TimeSpan.FromMinutes(_calendars.Settings.LinkRefreshMinutes);

        foreach (CalendarSource source in _calendars.Sources.Where(source => source.Kind != CalendarSourceKind.Local).ToList())
        {
            CalendarSourceStatus status = _calendars.StatusOf(source.Id);
            DateTimeOffset lastAttempt = status.LastAttemptAt ?? DateTimeOffset.MinValue;
            if (source.Kind == CalendarSourceKind.ICalLink && now - lastAttempt >= linkInterval)
            {
                await FetchAsync(source, full: true);
            }
            else if (source.Kind == CalendarSourceKind.Google && now - lastAttempt >= GoogleChangesInterval)
            {
                bool full = status.LastSuccessAt is null
                            || now - (status.LastFullFetchAt ?? DateTimeOffset.MinValue) >= GoogleFullInterval;
                await FetchAsync(source, full);
            }
        }
    }

    private async Task FetchAsync(CalendarSource source, bool full)
    {
        if (!_fetching.Add(source.Id))
        {
            return;
        }

        CalendarSourceStatus status = _calendars.StatusOf(source.Id);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        try
        {
            status = source.Kind == CalendarSourceKind.ICalLink
                ? await FetchLinkAsync(source, status)
                : await FetchGoogleAsync(source, status, full);
            status = status with { LastAttemptAt = now, LastSuccessAt = now, Error = null };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or FormatException or TaskCanceledException
                                       or InvalidOperationException)
        {
            status = status with { LastAttemptAt = now, Error = Describe(ex) };
        }
        finally
        {
            _fetching.Remove(source.Id);
        }

        // The source may have been removed while this fetch was running.
        if (_calendars.Sources.Any(candidate => candidate.Id == source.Id))
        {
            _calendars.SetStatus(source.Id, status);
        }
    }

    private async Task<CalendarSourceStatus> FetchLinkAsync(CalendarSource source, CalendarSourceStatus status)
    {
        Uri address = new(Unprotect(source.ProtectedSecret) ?? throw new InvalidOperationException("The link can't be read on this computer."));
        bool haveEvents = source.Calendars.Count > 0 && _calendars.Cache.EventsByCalendar.ContainsKey(source.Calendars[0].Id);
        ICalDownload download = await _network.DownloadICalAsync(
            address, haveEvents ? status.ETag : null, haveEvents ? status.LastModified : null, CancellationToken.None);
        if (download.NotModified)
        {
            return status with { ETag = download.ETag, LastModified = download.LastModified };
        }

        CalendarInfo calendar = source.Calendars[0];
        (DateTime from, DateTime to) = Range();
        string text = download.Text!;
        CalendarFeed feed = await Task.Run(() =>
            CalendarFeedParser.Parse(text, calendar.Id, from, to, _timeProvider.LocalTimeZone));

        if (feed.Name is { Length: > 0 } name && calendar.Name != name)
        {
            _calendars.SaveSource(source with { Calendars = new[] { calendar with { Name = name } } });
        }

        _calendars.SetEvents(calendar.Id, feed.Events);
        return status with { ETag = download.ETag, LastModified = download.LastModified, LastFullFetchAt = _timeProvider.GetUtcNow() };
    }

    private async Task<CalendarSourceStatus> FetchGoogleAsync(CalendarSource source, CalendarSourceStatus status, bool full)
    {
        string accessToken = await AccessTokenAsync(source);
        (DateTime from, DateTime to) = Range();
        TimeZoneInfo zone = _timeProvider.LocalTimeZone;

        if (full)
        {
            // New or renamed calendars appear; removed ones go (with their events).
            IReadOnlyList<GoogleCalendarEntry> entries = await _google.ListCalendarsAsync(accessToken, CancellationToken.None);
            source = source with { Calendars = entries.Select(entry => ToCalendarInfo(source.Id, entry)).ToList() };
            _calendars.SaveSource(source);
            source = _calendars.Sources.First(candidate => candidate.Id == source.Id);
        }

        DateTimeOffset? since = full ? null : status.LastSuccessAt - ChangesOverlap;
        foreach (CalendarInfo calendar in source.Calendars)
        {
            string googleId = calendar.Id[(source.Id.Length + 1)..];
            GoogleEventChanges changes = await _google.ListEventsAsync(
                accessToken, googleId, calendar.Id, from, to, since, zone, CancellationToken.None);
            if (full)
            {
                _calendars.SetEvents(calendar.Id, changes.Events);
            }
            else
            {
                _calendars.ApplyEventChanges(calendar.Id, changes.Events, changes.CancelledIds);
            }
        }

        return full ? status with { LastFullFetchAt = _timeProvider.GetUtcNow() } : status;
    }

    private async Task<string> AccessTokenAsync(CalendarSource source)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (_accessTokens.TryGetValue(source.Id, out (string Token, DateTimeOffset ExpiresAt) cached) && cached.ExpiresAt > now)
        {
            return cached.Token;
        }

        string refreshToken = Unprotect(source.ProtectedSecret)
                              ?? throw new InvalidOperationException("The sign-in can't be read on this computer. Connect the account again.");
        if (!TryGetGoogleClient(out string clientId, out string clientSecret))
        {
            throw new InvalidOperationException("The Google client ID and secret are missing in Settings.");
        }

        try
        {
            (string token, DateTimeOffset expiresAt) = await _google.RefreshAccessTokenAsync(clientId, clientSecret, refreshToken, CancellationToken.None);
            _accessTokens[source.Id] = (token, expiresAt);
            return token;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException(
                "Google ended the sign-in (in Testing mode this happens after 7 days). Disconnect and connect the account again.");
        }
    }

    private bool TryGetGoogleClient(out string clientId, out string clientSecret)
    {
        clientId = _calendars.Settings.GoogleClientId ?? string.Empty;
        clientSecret = Unprotect(_calendars.Settings.ProtectedGoogleClientSecret) ?? string.Empty;
        return clientId.Length > 0 && clientSecret.Length > 0;
    }

    private CalendarInfo ToCalendarInfo(string sourceId, GoogleCalendarEntry entry)
    {
        string color = entry.Color is { Length: 7 } hex && hex[0] == '#' ? hex.ToUpperInvariant() : _calendars.NextUnusedColor();
        return new CalendarInfo($"{sourceId}|{entry.Id}", entry.Summary, color);
    }

    private (DateTime From, DateTime To) Range()
    {
        DateTime today = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), _timeProvider.LocalTimeZone).Date;
        return (today.AddDays(-DaysBack), today.AddDays(DaysAhead));
    }

    private string Protect(string secret)
    {
        return Convert.ToBase64String(_protector.Protect(secret));
    }

    private string? Unprotect(string? protectedSecret)
    {
        if (string.IsNullOrEmpty(protectedSecret))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(Convert.FromBase64String(protectedSecret));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string Describe(Exception ex)
    {
        return ex switch
        {
            HttpRequestException { StatusCode: { } code } => $"the server answered {(int)code} {code}.",
            HttpRequestException => "no connection to the server.",
            TaskCanceledException => "the server took too long to answer.",
            FormatException => "the address didn't return a calendar file.",
            _ => ex.Message,
        };
    }
}
