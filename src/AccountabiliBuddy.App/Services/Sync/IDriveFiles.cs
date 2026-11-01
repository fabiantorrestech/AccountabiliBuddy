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

    /// <summary>Deletes the file with Drive id [id] (one the app made); a file that is already gone is fine.</summary>
    /// <exception cref="HttpRequestException">Network error, or Drive refused.</exception>
    Task DeleteAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// Writes a removed device's backup (ADR 55) to <c>Backups/[deviceName]/[fileName]</c> inside the app's folder, making the
    /// folders when they aren't there; a file of the same name is replaced.
    /// </summary>
    /// <returns>The file's Drive id.</returns>
    /// <exception cref="HttpRequestException">Network error, or Drive refused (a full Drive answers 403).</exception>
    Task<string> UploadBackupAsync(string deviceName, string fileName, byte[] content, CancellationToken cancellationToken);

    /// <summary>The backups under <c>Backups/</c> (every device's folder); empty when there are none.</summary>
    /// <exception cref="HttpRequestException">Network error, or Drive refused (e.g. signed out).</exception>
    Task<IReadOnlyList<DriveBackupInfo>> ListBackupsAsync(CancellationToken cancellationToken);
}
