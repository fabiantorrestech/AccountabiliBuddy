using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The only place the app talks to the internet (ADR 9): read-only downloads of iCal addresses the user added, and
/// Google sign-in plus read-only Calendar API calls for accounts the user connected. Requests carry nothing beyond
/// what the protocol needs: no activity data, no identifying headers, no cookies.
/// </summary>
public sealed class CalendarNetwork : IDisposable
{
    // A calendar file larger than this is not a calendar anyone plans with; refuse rather than fill memory.
    private const long MaxResponseBytes = 20 * 1024 * 1024;

    private readonly HttpClient _http = new(new HttpClientHandler { UseCookies = false })
    {
        Timeout = TimeSpan.FromSeconds(30),
        MaxResponseContentBufferSize = MaxResponseBytes,
    };

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

    public void Dispose()
    {
        _http.Dispose();
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
