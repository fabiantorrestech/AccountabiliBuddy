namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// Where the goal history files (month and year files, ADR 79 shared history, GS3b) are kept: the app's Drive folder or the user's own
/// relay. The history rules and the order of a run are the same for both (<see cref="GoalHistorySync"/>); only the limits differ.
/// </summary>
public interface IGoalHistoryStore
{
    /// <summary>"drive" or "relay": the key of this transport's state.</summary>
    string Transport { get; }

    /// <summary>The file name's extension, with the dot (the relay accepts only its own).</summary>
    string Extension { get; }

    /// <summary>The biggest file the store takes (the relay refuses over 4 MiB, ADR 46).</summary>
    long MaxFileBytes { get; }

    /// <summary>The most files one group can hold (the relay's 64); null for no limit.</summary>
    int? MaxFiles { get; }

    /// <summary>The most requests (list, get, put, delete) one pass makes; null for no cap. The relay allows 120 a minute; a pass stops at 40 and goes on 15 minutes later.</summary>
    int? MaxRequestsPerPass { get; }

    /// <summary>True when the store deletes files nobody touched for 30 days (the relay): own files are put again every 25 days.</summary>
    bool Expires { get; }

    /// <summary>True when this PC may delete its own history files here (Drive). The relay deletes only when a device leaves its group.</summary>
    bool CanDelete { get; }

    /// <summary>Every file of the group's folder or relay group (not only history files).</summary>
    Task<IReadOnlyList<GoalHistoryRemoteFile>> ListAsync(CancellationToken cancellationToken);

    /// <summary>The file's bytes; null when it is gone.</summary>
    Task<byte[]?> GetAsync(string name, CancellationToken cancellationToken);

    Task PutAsync(string name, byte[] bytes, CancellationToken cancellationToken);

    /// <summary>Deletes the file; true when it was deleted or already gone, false when this store never deletes.</summary>
    Task<bool> DeleteAsync(string name, CancellationToken cancellationToken);
}
