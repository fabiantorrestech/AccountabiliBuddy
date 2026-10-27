using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using AccountabiliBuddy.App.Services.Sync;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The only place the app talks to the internet (ADR 9, ADR 26): read-only downloads of iCal addresses the user added,
/// Google sign-in plus read-only Calendar API calls for accounts the user connected, and Google Drive calls for the
/// user's own sealed sync files (scope drive.file: only files the app made), and read-only GETs of the user's Beeminder
/// goals with the personal auth token they pasted (ADR 43), and the user's own sealed relay (ADR 46): HTTPS only, to
/// the one address the user typed, with either that address's certificate pinned by its SHA-256 fingerprint (a
/// mismatch fails the call; there is never a fallback to an unpinned connection) or a normal certificate check when no
/// pin is set. Relay calls carry the bearer token in the Authorization header only and never follow a redirect.
/// Requests carry nothing beyond what the protocol needs: no activity data in the clear (sync files are sealed), no
/// identifying headers, no cookies.
/// </summary>
public sealed class CalendarNetwork : IDisposable
{
    // A calendar file larger than this is not a calendar anyone plans with; refuse rather than fill memory.
    private const long MaxResponseBytes = 20 * 1024 * 1024;

    // ADR 43: the one Beeminder address the app ever calls.
    private const string BeeminderGoalsAddress = "https://www.beeminder.com/api/v1/users/me/goals.json";

    // A sealed sync file is at most 4 MiB on the relay; its listing and answers are far smaller than this.
    private const long MaxRelayResponseBytes = 5 * 1024 * 1024;
    private const int MaxRelayClients = 4;
    private static readonly TimeSpan RelayRequestTimeout = TimeSpan.FromSeconds(30);

    // The relay pings its event stream every 25 seconds; a stream silent for three pings is dead.
    private static readonly TimeSpan RelayStreamIdleTimeout = TimeSpan.FromSeconds(75);

    private readonly HttpClient _http;

    // Relay clients, one per certificate pin ("" = no pin). Separate from _http: no 30-second client timeout (the event
    // stream stays open), and each pinned client carries its own certificate check.
    private readonly Dictionary<string, RelayClientEntry> _relayClients = [];
    private readonly object _relayLock = new();

    public CalendarNetwork()
        : this(new HttpClientHandler { UseCookies = false })
    {
    }

    /// <param name="handler">Where requests go (the real network, or a fake in checks).</param>
    public CalendarNetwork(HttpMessageHandler handler)
    {
        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30),
            MaxResponseContentBufferSize = MaxResponseBytes,
        };
    }

    /// <summary>Downloads an .ics file, asking the server to skip it when unchanged.</summary>
    /// <exception cref="HttpRequestException">Network error or an unsuccessful status code.</exception>
    public async Task<ICalDownload> DownloadICalAsync(Uri address, string? etag, string? lastModified, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        if (etag is not null && EntityTagHeaderValue.TryParse(etag, out EntityTagHeaderValue? tag))
        {
            request.Headers.IfNoneMatch.Add(tag);
        }

        if (lastModified is not null && DateTimeOffset.TryParse(lastModified, out DateTimeOffset modified))
        {
            request.Headers.IfModifiedSince = modified;
        }

        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        string? newEtag = response.Headers.ETag?.ToString() ?? etag;
        string? newLastModified = response.Content.Headers.LastModified?.ToString("R") ?? lastModified;
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return new ICalDownload(true, null, newEtag, newLastModified);
        }

        response.EnsureSuccessStatusCode();
        string text = await response.Content.ReadAsStringAsync(cancellationToken);
        return new ICalDownload(false, text, newEtag, newLastModified);
    }

    /// <summary>GET with a bearer token, returning the JSON body.</summary>
    /// <exception cref="HttpRequestException">Network error or an unsuccessful status code (status in the exception).</exception>
    public async Task<JsonDocument> GetJsonAsync(Uri uri, string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        return await ReadJsonAsync(response, cancellationToken);
    }

    /// <summary>POST of a form (OAuth token and revoke endpoints), returning the JSON body.</summary>
    public async Task<JsonDocument> PostFormAsync(Uri uri, IReadOnlyDictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using HttpResponseMessage response = await _http.PostAsync(uri, content, cancellationToken);
        return await ReadJsonAsync(response, cancellationToken);
    }

    /// <summary>GET with a bearer token, returning the raw body (a Drive file's content).</summary>
    /// <exception cref="HttpRequestException">Network error or an unsuccessful status code.</exception>
    public async Task<byte[]> GetBytesAsync(Uri uri, string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase}", null, response.StatusCode);
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    /// <summary>A request with a bearer token and a body (Drive's create, upload and update), returning the JSON body.</summary>
    /// <exception cref="HttpRequestException">Network error or an unsuccessful status code.</exception>
    public async Task<JsonDocument> SendJsonAsync(HttpMethod method, Uri uri, string accessToken, HttpContent content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        return await ReadJsonAsync(response, cancellationToken);
    }

    /// <summary>
    /// Reads the user's Beeminder goals (ADR 43): one GET, nothing written to Beeminder and nothing about the user sent.
    /// Beeminder takes the personal auth token in the query string; the address is never logged or shown, and errors
    /// carry only the status code.
    /// </summary>
    /// <exception cref="HttpRequestException">Network error or an unsuccessful status code (status in the exception).</exception>
    public async Task<JsonDocument> GetBeeminderGoalsAsync(string authToken, CancellationToken cancellationToken)
    {
        var uri = new Uri(BeeminderGoalsAddress + "?auth_token=" + Uri.EscapeDataString(authToken));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        return await ReadJsonAsync(response, cancellationToken);
    }

    /// <summary>Stores this device's sealed file on the relay (PUT). The first PUT for a new group makes the group.</summary>
    /// <returns>The etag the relay gives the stored bytes (unquoted base64url SHA-256).</returns>
    /// <exception cref="HttpRequestException">Network or TLS error (a pin mismatch has an AuthenticationException inside), or a status code that isn't a success (in the exception).</exception>
    public async Task<string> PutRelayFileAsync(RelayAccess access, string name, byte[] sealedBytes, CancellationToken cancellationToken)
    {
        using var body = new ByteArrayContent(sealedBytes);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using HttpResponseMessage response = await SendRelayAsync(
            access, HttpMethod.Put, RelayFilePath(access, name), body, HttpCompletionOption.ResponseContentRead, cancellationToken);
        ThrowIfRefused(response);
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return json.RootElement.GetProperty("etag").GetString() ?? string.Empty;
    }

    /// <summary>The group's files on the relay. A group nobody has written to yet (404) has none.</summary>
    /// <exception cref="HttpRequestException">As for <see cref="PutRelayFileAsync"/>.</exception>
    public async Task<IReadOnlyList<RelayFileInfo>> ListRelayFilesAsync(RelayAccess access, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendRelayAsync(
            access, HttpMethod.Get, $"v1/groups/{access.GroupId}/files", null, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        ThrowIfRefused(response);
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var files = new List<RelayFileInfo>();
        foreach (JsonElement file in json.RootElement.EnumerateArray())
        {
            files.Add(new RelayFileInfo(
                file.GetProperty("name").GetString() ?? string.Empty,
                file.GetProperty("etag").GetString() ?? string.Empty,
                file.GetProperty("size").GetInt64(),
                file.GetProperty("at").GetDateTimeOffset()));
        }

        return files;
    }

    /// <summary>One sealed file from the relay; null when it isn't there (404).</summary>
    /// <exception cref="HttpRequestException">As for <see cref="PutRelayFileAsync"/>.</exception>
    public async Task<RelayDownload?> GetRelayFileAsync(RelayAccess access, string name, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendRelayAsync(
            access, HttpMethod.Get, RelayFilePath(access, name), null, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        ThrowIfRefused(response);
        // The ETag header is quoted; the listing and the events give it unquoted.
        string? etag = response.Headers.ETag?.Tag.Trim('"');
        return new RelayDownload(await response.Content.ReadAsByteArrayAsync(cancellationToken), etag);
    }

    /// <summary>Removes this device's file from the relay (when it leaves the group). A file that is already gone is fine.</summary>
    /// <exception cref="HttpRequestException">As for <see cref="PutRelayFileAsync"/>.</exception>
    public async Task DeleteRelayFileAsync(RelayAccess access, string name, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendRelayAsync(
            access, HttpMethod.Delete, RelayFilePath(access, name), null, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            ThrowIfRefused(response);
        }
    }

    /// <summary>
    /// The relay's push stream (server-sent events): one <see cref="RelayChange"/> per "changed" event, until the relay
    /// or the network closes it. Ends normally when the relay closes the stream.
    /// </summary>
    /// <param name="onConnected">Called once the relay has accepted the stream.</param>
    /// <exception cref="HttpRequestException">As for <see cref="PutRelayFileAsync"/>, or the stream went quiet for 75 seconds.</exception>
    public async IAsyncEnumerable<RelayChange> ReadRelayEventsAsync(
        RelayAccess access, Action onConnected, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // The client stays in use for as long as the stream is open, so it can't be evicted and disposed under it.
        RelayClientEntry lease = AcquireRelayClient(access.Pin);
        try
        {
            using HttpResponseMessage response = await SendRelayAsync(
                access, HttpMethod.Get, $"v1/groups/{access.GroupId}/events", null, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            ThrowIfRefused(response);
            onConnected();
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            string? eventName = null;
            string? data = null;
            while (true)
            {
                string? line = await ReadStreamLineAsync(reader, cancellationToken);
                if (line is null)
                {
                    yield break;
                }

                if (line.Length == 0)
                {
                    if (eventName == "changed" && TryParseRelayChange(data, out RelayChange? change))
                    {
                        yield return change;
                    }

                    eventName = null;
                    data = null;
                }
                else if (line.StartsWith("event:", StringComparison.Ordinal))
                {
                    eventName = line[6..].Trim();
                }
                else if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    data = line[5..].Trim();
                }

                // Anything else (":ok", ":ping") is a comment.
            }
        }
        finally
        {
            ReleaseRelayClient(lease);
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        lock (_relayLock)
        {
            foreach (RelayClientEntry entry in _relayClients.Values)
            {
                entry.Client.Dispose();
            }

            _relayClients.Clear();
        }
    }

    private static string RelayFilePath(RelayAccess access, string name)
    {
        return $"v1/groups/{access.GroupId}/files/{Uri.EscapeDataString(name)}";
    }

    private async Task<HttpResponseMessage> SendRelayAsync(
        RelayAccess access, HttpMethod method, string path, HttpContent? content, HttpCompletionOption completion, CancellationToken cancellationToken)
    {
        // HTTPS only: a plain address would send the token in the clear.
        if (access.BaseAddress.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("The relay address must be https.");
        }

        using var request = new HttpRequestMessage(method, new Uri(access.BaseAddress, path))
        {
            Content = content,
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access.Token);

        // The timeout covers the headers and, for a buffered answer, the body. Once an event stream's headers are in,
        // this source is disposed and the stream has its own idle check.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RelayRequestTimeout);
        RelayClientEntry entry = AcquireRelayClient(access.Pin);
        try
        {
            return await entry.Client.SendAsync(request, completion, timeout.Token);
        }
        finally
        {
            ReleaseRelayClient(entry);
        }
    }

    private static void ThrowIfRefused(HttpResponseMessage response)
    {
        // A redirect is never followed (it could send the token elsewhere), so it is an error like any other status.
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"The relay answered {(int)response.StatusCode}", null, response.StatusCode);
        }
    }

    // The client for a pin, marked in use until ReleaseRelayClient. Past MaxRelayClients the least recently used clients
    // that nothing is using (no call or open stream) are disposed; one in use is never touched.
    private RelayClientEntry AcquireRelayClient(string? pin)
    {
        string key = pin ?? string.Empty;
        lock (_relayLock)
        {
            if (_relayClients.TryGetValue(key, out RelayClientEntry? existing))
            {
                existing.Active++;
                existing.LastUsed = Environment.TickCount64;
                return existing;
            }

            while (_relayClients.Count >= MaxRelayClients)
            {
                KeyValuePair<string, RelayClientEntry>? oldest = _relayClients
                    .Where(pair => pair.Value.Active == 0)
                    .OrderBy(pair => pair.Value.LastUsed)
                    .Select(pair => (KeyValuePair<string, RelayClientEntry>?)pair)
                    .FirstOrDefault();
                if (oldest is null)
                {
                    break; // All in use: go over the limit for now.
                }

                oldest.Value.Value.Client.Dispose();
                _relayClients.Remove(oldest.Value.Key);
            }

            var handler = new SocketsHttpHandler
            {
                UseCookies = false,
                AllowAutoRedirect = false,
                ConnectTimeout = TimeSpan.FromSeconds(15),
            };
            if (pin is not null)
            {
                // The pinned certificate is the only check: the name and the chain don't matter for a self-signed
                // certificate. Anything else, including no certificate, fails the connection (fail closed).
                handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, _) => PinMatches(certificate, pin);
            }

            var client = new HttpClient(handler, disposeHandler: true)
            {
                Timeout = Timeout.InfiniteTimeSpan,
                MaxResponseContentBufferSize = MaxRelayResponseBytes,
            };
            var entry = new RelayClientEntry(client) { Active = 1, LastUsed = Environment.TickCount64 };
            _relayClients[key] = entry;
            return entry;
        }
    }

    private void ReleaseRelayClient(RelayClientEntry entry)
    {
        lock (_relayLock)
        {
            entry.Active--;
        }
    }

    private sealed class RelayClientEntry(HttpClient client)
    {
        public HttpClient Client { get; } = client;

        public int Active { get; set; }

        public long LastUsed { get; set; }
    }

    private static bool PinMatches(X509Certificate? certificate, string pin)
    {
        if (certificate is null)
        {
            return false;
        }

        byte[] actual = Encoding.ASCII.GetBytes(CertificateIdentity.Fingerprint(certificate));
        return CryptographicOperations.FixedTimeEquals(actual, Encoding.ASCII.GetBytes(pin));
    }

    // One line of the stream, or null when it ended. A stream with nothing (not even a ping) for the idle timeout is dead.
    private static async Task<string?> ReadStreamLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        idle.CancelAfter(RelayStreamIdleTimeout);
        try
        {
            return await reader.ReadLineAsync(idle.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HttpRequestException("The relay's event stream went quiet.");
        }
    }

    private static bool TryParseRelayChange(string? data, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out RelayChange? change)
    {
        change = null;
        if (string.IsNullOrEmpty(data))
        {
            return false;
        }

        try
        {
            using JsonDocument json = JsonDocument.Parse(data);
            string? name = json.RootElement.GetProperty("name").GetString();
            string? etag = json.RootElement.GetProperty("etag").GetString();
            if (name is null || etag is null)
            {
                return false;
            }

            change = new RelayChange(name, etag);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase}", null, response.StatusCode);
        }

        return JsonDocument.Parse(body.Length == 0 ? "{}" : body);
    }
}
