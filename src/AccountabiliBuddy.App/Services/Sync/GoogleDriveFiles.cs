using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// The app's Drive folder over Drive v3 (ADR 26), through <see cref="CalendarNetwork"/>, the one auditable network
/// class. With the drive.file scope the app sees only files it made, so every install of the same OAuth client finds
/// the same folder ("AccountabiliBuddy sync") and each other's files, and nothing else in the user's Drive.
/// </summary>
public sealed class GoogleDriveFiles : IDriveFiles
{
    public const string FolderName = "AccountabiliBuddy sync";
    private const string FolderMime = "application/vnd.google-apps.folder";

    private readonly CalendarNetwork _network;
    private readonly GoogleEndpoints _endpoints;
    private readonly Func<CancellationToken, Task<string>> _accessToken;
    private string? _folderId;

    /// <param name="accessToken">A fresh access token for the signed-in Drive account.</param>
    public GoogleDriveFiles(CalendarNetwork network, GoogleEndpoints endpoints, Func<CancellationToken, Task<string>> accessToken)
    {
        _network = network;
        _endpoints = endpoints;
        _accessToken = accessToken;
    }

    public async Task<IReadOnlyList<DriveFileInfo>> ListAsync(CancellationToken cancellationToken)
    {
        string token = await _accessToken(cancellationToken);
        string folder = await FolderAsync(token, cancellationToken);
        var files = new List<DriveFileInfo>();
        string? page = null;
        do
        {
            var query = new Dictionary<string, string>
            {
                ["q"] = $"'{folder}' in parents and trashed=false",
                ["fields"] = "nextPageToken,files(id,name,modifiedTime,size)",
                ["pageSize"] = "100",
                ["spaces"] = "drive",
            };
            if (page is not null)
            {
                query["pageToken"] = page;
            }

            using JsonDocument json = await _network.GetJsonAsync(Uri("files", query), token, cancellationToken);
            foreach (JsonElement file in json.RootElement.GetProperty("files").EnumerateArray())
            {
                files.Add(new DriveFileInfo(
                    file.GetProperty("id").GetString()!,
                    file.GetProperty("name").GetString()!,
                    DateTimeOffset.Parse(file.GetProperty("modifiedTime").GetString()!, CultureInfo.InvariantCulture),
                    file.TryGetProperty("size", out JsonElement size) && long.TryParse(size.GetString(), out long bytes) ? bytes : 0));
            }

            page = json.RootElement.TryGetProperty("nextPageToken", out JsonElement next) ? next.GetString() : null;
        }
        while (page is not null);

        return files;
    }

    public async Task<byte[]> DownloadAsync(string id, CancellationToken cancellationToken)
    {
        string token = await _accessToken(cancellationToken);
        return await _network.GetBytesAsync(Uri($"files/{System.Uri.EscapeDataString(id)}", new Dictionary<string, string> { ["alt"] = "media" }), token, cancellationToken);
    }

    public async Task<string> UploadAsync(string name, byte[] content, string? existingId, CancellationToken cancellationToken)
    {
        string token = await _accessToken(cancellationToken);
        if (existingId is not null)
        {
            using var body = new ByteArrayContent(content);
            body.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            using JsonDocument updated = await _network.SendJsonAsync(
                HttpMethod.Patch, UploadUri($"files/{System.Uri.EscapeDataString(existingId)}", "media"), token, body, cancellationToken);
            return updated.RootElement.GetProperty("id").GetString()!;
        }

        string folder = await FolderAsync(token, cancellationToken);
        string metadata = JsonSerializer.Serialize(new { name, parents = new[] { folder } });
        using var multipart = new MultipartContent("related");
        var meta = new StringContent(metadata, Encoding.UTF8, "application/json");
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(meta);
        multipart.Add(file);
        using JsonDocument created = await _network.SendJsonAsync(HttpMethod.Post, UploadUri("files", "multipart"), token, multipart, cancellationToken);
        return created.RootElement.GetProperty("id").GetString()!;
    }

    // The app's folder: found by name among the files the app made, else made now. Kept for this run.
    private async Task<string> FolderAsync(string token, CancellationToken cancellationToken)
    {
        if (_folderId is not null)
        {
            return _folderId;
        }

        using JsonDocument found = await _network.GetJsonAsync(Uri("files", new Dictionary<string, string>
        {
            ["q"] = $"name='{FolderName}' and mimeType='{FolderMime}' and trashed=false",
            ["fields"] = "files(id)",
            ["spaces"] = "drive",
        }), token, cancellationToken);
        JsonElement files = found.RootElement.GetProperty("files");
        if (files.GetArrayLength() > 0)
        {
            return _folderId = files[0].GetProperty("id").GetString()!;
        }

        using var body = new StringContent(JsonSerializer.Serialize(new { name = FolderName, mimeType = FolderMime }), Encoding.UTF8, "application/json");
        using JsonDocument created = await _network.SendJsonAsync(
            HttpMethod.Post, Uri("files", new Dictionary<string, string> { ["fields"] = "id" }), token, body, cancellationToken);
        return _folderId = created.RootElement.GetProperty("id").GetString()!;
    }

    private Uri Uri(string path, IReadOnlyDictionary<string, string> query) => WithQuery(new Uri(_endpoints.DriveApi, path), query);

    private Uri UploadUri(string path, string uploadType) =>
        WithQuery(new Uri(_endpoints.DriveUpload, path), new Dictionary<string, string> { ["uploadType"] = uploadType, ["fields"] = "id" });

    private static Uri WithQuery(Uri uri, IReadOnlyDictionary<string, string> query) =>
        new($"{uri}?{string.Join("&", query.Select(pair => $"{System.Uri.EscapeDataString(pair.Key)}={System.Uri.EscapeDataString(pair.Value)}"))}");
}
