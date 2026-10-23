namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>The app's own folder in Google Drive (ADR 26): list it, download a file, write one.</summary>
public interface IDriveFiles
{
    /// <exception cref="HttpRequestException">Network error, or Drive refused (e.g. signed out).</exception>
    Task<IReadOnlyList<DriveFileInfo>> ListAsync(CancellationToken cancellationToken);

    Task<byte[]> DownloadAsync(string id, CancellationToken cancellationToken);

    /// <summary>Writes [name] (a new file when [existingId] is null, else that file's new content).</summary>
    /// <returns>The file's Drive id.</returns>
    Task<string> UploadAsync(string name, byte[] content, string? existingId, CancellationToken cancellationToken);
}
