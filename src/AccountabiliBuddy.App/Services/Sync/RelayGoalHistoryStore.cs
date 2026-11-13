namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// The goal history files on the user's own relay (ADR 46), through <see cref="CalendarNetwork"/>. The relay accepts only names of 12 hex
/// characters plus ".absync", at most 64 files and 4 MiB a file, deletes files nobody touched for 30 days, and this PC deletes its
/// files there only when it leaves the group: so the files are put again every 25 days and a rolled-up month file is simply not put
/// again (it expires).
/// </summary>
public sealed class RelayGoalHistoryStore : IGoalHistoryStore
{
    /// <summary>The relay's own limits (ADR 46).</summary>
    public const long RelayMaxFileBytes = 4 * 1024 * 1024;

    public const int RelayMaxFiles = 64;

    private readonly CalendarNetwork _network;
    private readonly RelayAccess _access;

    public RelayGoalHistoryStore(CalendarNetwork network, RelayAccess access)
    {
        _network = network;
        _access = access;
    }

    public string Transport => "relay";

    public string Extension => SyncFolderService.Extension;

    public long MaxFileBytes => RelayMaxFileBytes;

    public int? MaxFiles => RelayMaxFiles;

    public bool Expires => true;

    public bool CanDelete => false;

    public async Task<IReadOnlyList<GoalHistoryRemoteFile>> ListAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<RelayFileInfo> files = await _network.ListRelayFilesAsync(_access, cancellationToken);
        return files.Select(file => new GoalHistoryRemoteFile(file.Name, file.At, file.Size, file.ETag)).ToList();
    }

    public async Task<byte[]?> GetAsync(string name, CancellationToken cancellationToken)
    {
        return (await _network.GetRelayFileAsync(_access, name, cancellationToken))?.Bytes;
    }

    public async Task PutAsync(string name, byte[] bytes, CancellationToken cancellationToken)
    {
        await _network.PutRelayFileAsync(_access, name, bytes, cancellationToken);
    }

    public Task<bool> DeleteAsync(string name, CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }
}
