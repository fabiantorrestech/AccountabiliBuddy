using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Google sign-in and the read-only Calendar API, as a small client over <see cref="CalendarNetwork"/> (ADR 9).
/// <list type="bullet">
/// <item>Sign-in: the installed-app flow. Google's page opens in the browser and hands back to a one-shot listener
/// on 127.0.0.1 (any free port); PKCE (S256) and a random state protect the hand-off. Scope: calendar.readonly.</item>
/// <item>Events: singleEvents (repeats expanded) within a date range; incremental fetches use updatedMin with
/// showDeleted, so only changes come back.</item>
/// </list>
/// The Google Cloud project (client id and secret) is the user's own.
/// </summary>
public sealed class GoogleCalendarClient
{
    public const string Scope = "https://www.googleapis.com/auth/calendar.readonly";

    private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(5);

    private readonly CalendarNetwork _network;
    private readonly GoogleEndpoints _endpoints;
    private readonly TimeProvider _timeProvider;

    public GoogleCalendarClient(CalendarNetwork network, GoogleEndpoints endpoints, TimeProvider timeProvider)
    {
        _network = network;
        _endpoints = endpoints;
        _timeProvider = timeProvider;
    }

    /// <param name="openBrowser">Opens the sign-in page; returns false if it couldn't.</param>
    /// <exception cref="GoogleSignInException">Cancelled, denied, timed out, or Google returned no refresh token.</exception>
    /// <param name="scope">The access asked for; Calendar read-only unless given (Drive sync asks for drive.file, ADR 26).</param>
    public async Task<GoogleTokens> SignInAsync(
        string clientId, string clientSecret, Func<Uri, bool> openBrowser, CancellationToken cancellationToken, string? scope = null)
    {
        string verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        string state = Base64Url(RandomNumberGenerator.GetBytes(16));

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            string redirectUri = $"http://127.0.0.1:{port}/";
            Uri authorize = WithQuery(_endpoints.Authorize, new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["redirect_uri"] = redirectUri,
                ["response_type"] = "code",
                ["scope"] = scope ?? Scope,
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
                ["state"] = state,
                ["access_type"] = "offline",
                ["prompt"] = "consent",
            });

            if (!openBrowser(authorize))
            {
                throw new GoogleSignInException("The browser could not be opened.");
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(SignInTimeout);
            IReadOnlyDictionary<string, string> callback = await ReceiveCallbackAsync(listener, timeout.Token);

            if (callback.TryGetValue("error", out string? error))
            {
                throw new GoogleSignInException(error == "access_denied" ? "Access was not allowed." : $"Google said: {error}.");
            }

            if (!callback.TryGetValue("state", out string? returnedState) || returnedState != state
                || !callback.TryGetValue("code", out string? code))
            {
                throw new GoogleSignInException("The sign-in answer didn't match this request.");
            }

            using JsonDocument token = await _network.PostFormAsync(_endpoints.Token, new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["redirect_uri"] = redirectUri,
                ["grant_type"] = "authorization_code",
                ["code_verifier"] = verifier,
            }, cancellationToken);

            string? refreshToken = OptionalString(token.RootElement, "refresh_token");
            if (refreshToken is null)
            {
                throw new GoogleSignInException("Google returned no refresh token.");
            }

            return new GoogleTokens(refreshToken, RequiredString(token.RootElement, "access_token"), ExpiresAt(token.RootElement));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GoogleSignInException("Sign-in took longer than 5 minutes.");
        }
        finally
        {
            listener.Stop();
        }
    }

    public async Task<(string AccessToken, DateTimeOffset ExpiresAt)> RefreshAccessTokenAsync(
        string clientId, string clientSecret, string refreshToken, CancellationToken cancellationToken)
    {
        using JsonDocument token = await _network.PostFormAsync(_endpoints.Token, new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
        }, cancellationToken);
        return (RequiredString(token.RootElement, "access_token"), ExpiresAt(token.RootElement));
    }

    public async Task<IReadOnlyList<GoogleCalendarEntry>> ListCalendarsAsync(string accessToken, CancellationToken cancellationToken)
    {
        var calendars = new List<GoogleCalendarEntry>();
        string? pageToken = null;
        do
        {
            var query = new Dictionary<string, string> { ["maxResults"] = "250" };
            if (pageToken is not null)
            {
                query["pageToken"] = pageToken;
            }

            using JsonDocument page = await _network.GetJsonAsync(
                WithQuery(new Uri(_endpoints.CalendarApi, "users/me/calendarList"), query), accessToken, cancellationToken);
            foreach (JsonElement item in Items(page.RootElement))
            {
                calendars.Add(new GoogleCalendarEntry(
                    RequiredString(item, "id"),
                    OptionalString(item, "summaryOverride") ?? OptionalString(item, "summary") ?? "(no name)",
                    OptionalString(item, "backgroundColor"),
                    item.TryGetProperty("primary", out JsonElement primary) && primary.ValueKind == JsonValueKind.True));
            }

            pageToken = OptionalString(page.RootElement, "nextPageToken");
        }
        while (pageToken is not null);

        return calendars;
    }

    /// <param name="calendarId">Google's calendar id.</param>
    /// <param name="appCalendarId">The id events get in this app.</param>
    /// <param name="updatedMin">Only events changed since then (incremental); null for everything in range.</param>
    public async Task<GoogleEventChanges> ListEventsAsync(
        string accessToken,
        string calendarId,
        string appCalendarId,
        DateTime fromLocal,
        DateTime toLocal,
        DateTimeOffset? updatedMin,
        TimeZoneInfo zone,
        CancellationToken cancellationToken)
    {
        var events = new List<CalendarEvent>();
        var cancelled = new List<string>();
        string? pageToken = null;
        do
        {
            var query = new Dictionary<string, string>
            {
                ["singleEvents"] = "true",
                ["maxResults"] = "2500",
                ["timeMin"] = Rfc3339(TimeZoneInfo.ConvertTimeToUtc(fromLocal, zone)),
                ["timeMax"] = Rfc3339(TimeZoneInfo.ConvertTimeToUtc(toLocal, zone)),
            };
            if (updatedMin is DateTimeOffset since)
            {
                query["updatedMin"] = Rfc3339(since.UtcDateTime);
                query["showDeleted"] = "true";
            }

            if (pageToken is not null)
            {
                query["pageToken"] = pageToken;
            }

            Uri uri = WithQuery(new Uri(_endpoints.CalendarApi, $"calendars/{Uri.EscapeDataString(calendarId)}/events"), query);
            using JsonDocument page = await _network.GetJsonAsync(uri, accessToken, cancellationToken);
            foreach (JsonElement item in Items(page.RootElement))
            {
                string id = RequiredString(item, "id");
                if (OptionalString(item, "status") == "cancelled")
                {
                    cancelled.Add(id);
                    continue;
                }

                if (TryReadTime(item, "start", zone, out DateTime start, out bool isAllDay)
                    && TryReadTime(item, "end", zone, out DateTime end, out _))
                {
                    string title = OptionalString(item, "summary")?.Trim() is { Length: > 0 } summary ? summary : "(no title)";
                    events.Add(new CalendarEvent(id, appCalendarId, title, start, end > start ? end : start, isAllDay));
                }
            }

            pageToken = OptionalString(page.RootElement, "nextPageToken");
        }
        while (pageToken is not null);

        return new GoogleEventChanges(events, cancelled);
    }

    public async Task RevokeAsync(string token, CancellationToken cancellationToken)
    {
        using JsonDocument _ = await _network.PostFormAsync(
            _endpoints.Revoke, new Dictionary<string, string> { ["token"] = token }, cancellationToken);
    }

    /// <summary>Accepts browser requests on the loopback listener until one carries the sign-in answer.</summary>
    private static async Task<IReadOnlyDictionary<string, string>> ReceiveCallbackAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (true)
        {
            using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
            await using NetworkStream stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            string? requestLine = await reader.ReadLineAsync(cancellationToken);
            Dictionary<string, string> query = ParseQuery(requestLine);

            bool isAnswer = query.ContainsKey("code") || query.ContainsKey("error");
            string body = isAnswer
                ? "<html><body style=\"font-family:sans-serif\"><h2>AccountabiliBuddy</h2><p>You can close this tab and go back to the app.</p></body></html>"
                : string.Empty;
            string status = isAnswer ? "200 OK" : "404 Not Found";
            byte[] response = Encoding.UTF8.GetBytes(
                $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}");
            await stream.WriteAsync(response, cancellationToken);

            if (isAnswer)
            {
                return query;
            }
        }
    }

    private static Dictionary<string, string> ParseQuery(string? requestLine)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] parts = requestLine?.Split(' ') ?? Array.Empty<string>();
        if (parts.Length < 2)
        {
            return result;
        }

        int questionMark = parts[1].IndexOf('?');
        if (questionMark < 0)
        {
            return result;
        }

        foreach (string pair in parts[1][(questionMark + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=');
            string key = Uri.UnescapeDataString(equals < 0 ? pair : pair[..equals]);
            string value = equals < 0 ? string.Empty : Uri.UnescapeDataString(pair[(equals + 1)..].Replace('+', ' '));
            result[key] = value;
        }

        return result;
    }

    private static bool TryReadTime(JsonElement item, string property, TimeZoneInfo zone, out DateTime local, out bool isAllDay)
    {
        local = default;
        isAllDay = false;
        if (!item.TryGetProperty(property, out JsonElement time))
        {
            return false;
        }

        if (OptionalString(time, "date") is { } date
            && DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day))
        {
            local = day.ToDateTime(TimeOnly.MinValue);
            isAllDay = true;
            return true;
        }

        if (OptionalString(time, "dateTime") is { } dateTime
            && DateTimeOffset.TryParse(dateTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset value))
        {
            local = DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(value.UtcDateTime, zone), DateTimeKind.Unspecified);
            return true;
        }

        return false;
    }

    private DateTimeOffset ExpiresAt(JsonElement token)
    {
        int seconds = token.TryGetProperty("expires_in", out JsonElement value) && value.TryGetInt32(out int parsed) ? parsed : 3600;
        // Refresh a minute early so a request never goes out with a token that expires on the way.
        return _timeProvider.GetUtcNow().AddSeconds(seconds - 60);
    }

    private static IEnumerable<JsonElement> Items(JsonElement root)
    {
        return root.TryGetProperty("items", out JsonElement items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray()
            : Enumerable.Empty<JsonElement>();
    }

    private static string? OptionalString(JsonElement element, string property)
    {
        return element.ValueKind == JsonValueKind.Object
               && element.TryGetProperty(property, out JsonElement value)
               && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string RequiredString(JsonElement element, string property)
    {
        return OptionalString(element, property) ?? throw new JsonException($"Google's answer has no '{property}'.");
    }

    private static Uri WithQuery(Uri baseUri, IReadOnlyDictionary<string, string> query)
    {
        string encoded = string.Join("&", query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return new Uri($"{baseUri}?{encoded}");
    }

    private static string Rfc3339(DateTime utc)
    {
        return utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private static string Base64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
