namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>The goal history files in the app's own Drive folder (ADR 26, drive.file scope), next to the sync files, under their own extension.</summary>
public sealed class DriveGoalHistoryStore : IGoalHistoryStore
{
    public const string FileExtension = ".abgh";

    private readonly IDriveFiles _drive;
    private readonly Dictionary<string, string> _ids = [];

    public DriveGoalHistoryStore(IDriveFiles drive)
    {
        _drive = drive;
    }

    public string Transport => "drive";

    public string Extension => FileExtension;

    public long MaxFileBytes => 16 * 1024 * 1024;

    public int? MaxFiles => null;

    public int? MaxRequestsPerPass => null;

    public bool Expires => false;

    public bool CanDelete => true;

    public async Task<IReadOnlyList<GoalHistoryRemoteFile>> ListAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<DriveFileInfo> files = await _drive.ListAsync(cancellationToken);
        _ids.Clear();
        foreach (DriveFileInfo file in files)
        {
            _ids[file.Name] = file.Id;
        }

        return files.Select(file => new GoalHistoryRemoteFile(file.Name, file.ModifiedAt, file.Size, null)).ToList();
    }

    public async Task<byte[]?> GetAsync(string name, CancellationToken cancellationToken)
    {
        return _ids.TryGetValue(name, out string? id) ? await _drive.DownloadAsync(id, cancellationToken) : null;
    }

    public async Task PutAsync(string name, byte[] bytes, CancellationToken cancellationToken)
    {
        _ids.TryGetValue(name, out string? existing);
        _ids[name] = await _drive.UploadAsync(name, bytes, existing, cancellationToken);
    }

    public async Task<bool> DeleteAsync(string name, CancellationToken cancellationToken)
    {
        if (_ids.TryGetValue(name, out string? id))
        {
            await _drive.DeleteAsync(id, cancellationToken);
            _ids.Remove(name);
        }

        return true;
    }
}
