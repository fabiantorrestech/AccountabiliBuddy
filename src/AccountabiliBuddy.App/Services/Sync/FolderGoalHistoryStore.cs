namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// The goal history files in the user's sync folder (ADR 22 way B, ADR 79, NG1): the same names and <c>.abgh</c> extension as on Drive, kept
/// next to the sync files, which the regular folder reader skips by extension. The phone's <c>FolderGoalHistoryStore</c> is the twin. Files
/// are written beside the target and moved over it, so a reader never sees half a file. A folder that can't be listed throws, and the caller
/// skips the pass (tried again at the next one), never guessing at what is in it.
/// </summary>
public sealed class FolderGoalHistoryStore : IGoalHistoryStore
{
    private readonly string _folder;

    public FolderGoalHistoryStore(string folder)
    {
        _folder = folder;
    }

    public string Transport => "folder";

    public string Extension => DriveGoalHistoryStore.FileExtension;

    public long MaxFileBytes => 16 * 1024 * 1024;

    public int? MaxFiles => null;

    public int? MaxRequestsPerPass => null;

    public bool Expires => false;

    public bool CanDelete => true;

    public Task<IReadOnlyList<GoalHistoryRemoteFile>> ListAsync(CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<GoalHistoryRemoteFile>>(
            () => new DirectoryInfo(_folder).GetFiles()
                .Select(file => new GoalHistoryRemoteFile(file.Name, new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero), file.Length, null))
                .ToList(),
            cancellationToken);
    }

    public Task<byte[]?> GetAsync(string name, CancellationToken cancellationToken)
    {
        return Task.Run<byte[]?>(
            () =>
            {
                string path = PathFor(name);
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            },
            cancellationToken);
    }

    public Task PutAsync(string name, byte[] bytes, CancellationToken cancellationToken)
    {
        return Task.Run(
            () =>
            {
                string target = PathFor(name);
                string temporary = target + ".tmp";
                try
                {
                    File.WriteAllBytes(temporary, bytes);
                    File.Move(temporary, target, overwrite: true);
                }
                catch
                {
                    // No half-written leftover in the shared folder.
                    try { File.Delete(temporary); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                    throw;
                }
            },
            cancellationToken);
    }

    public Task<bool> DeleteAsync(string name, CancellationToken cancellationToken)
    {
        return Task.Run(
            () =>
            {
                // File.Delete says nothing when the file is already gone.
                File.Delete(PathFor(name));
                return true;
            },
            cancellationToken);
    }

    // A name is 12 hex characters and an extension; anything with a path in it never leaves the folder.
    private string PathFor(string name)
    {
        if (name != Path.GetFileName(name))
        {
            throw new ArgumentException("A history file name has no path in it.", nameof(name));
        }

        return Path.Combine(_folder, name);
    }
}
