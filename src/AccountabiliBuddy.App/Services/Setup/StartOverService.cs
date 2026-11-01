using System.Text.Json;
using AccountabiliBuddy.Core.Storage;

namespace AccountabiliBuddy.App.Services.Setup;

/// <summary>
/// "Start over" / "Start from scratch" (ADR 54, 10): the app's own data is cleared and the first-run assistant opens again.
/// It runs in two parts, because the database can't be deleted while this copy of the app has it open.
/// <list type="number">
/// <item><see cref="PrepareAsync"/> (this run): refused during a focus session the user started or when the typed confirmation
/// isn't "start over"; copies the database to the backup folder with the same mechanism as the copy before a migration
/// (<see cref="LocalDatabase.BackupAsync"/>); then writes a marker file saying which copy it made. The caller restarts the app.</item>
/// <item><see cref="RunPending"/> (the next start, before the database is opened): if a fresh marker names a copy that
/// exists, it deletes the database and the icon cache, and the marker. Nothing else.</item>
/// </list>
/// What is never touched: the <c>backups</c> folder (so the copy survives), the browser extension link (<c>browser-extension</c>,
/// <c>native-host</c>), the folder of a relay hosted on this PC (<c>relay</c>: its certificate is pinned by the other devices), and
/// everything outside the data folder, including Cold Turkey. Only the names in <see cref="ClearedFiles"/> and
/// <see cref="ClearedFolders"/> are ever deleted, inside the folder the app opened its database in; a folder that doesn't hold
/// the app's database is left alone.
/// </summary>
public sealed class StartOverService
{
    /// <summary>The words the user types to confirm.</summary>
    public const string ConfirmPhrase = "start over";

    /// <summary>Why starting over is refused during a focus session.</summary>
    public const string LockedReason = "Starting over is locked while a focus session you started is running.";

    private const string MarkerFileName = "start-over.pending";
    private const string DatabaseFileName = "accountabilibuddy.db";

    // A marker left by a run that crashed before restarting must not wipe the data of a later, unrelated start.
    private static readonly TimeSpan MarkerLifetime = TimeSpan.FromMinutes(10);

    // How many start-over copies of the database are kept (the newest).
    private const int KeptCopies = 3;

    // The old copy of the app may still hold the database for a moment after it asked for the restart.
    private const int DeleteAttempts = 12;
    private static readonly TimeSpan DeleteRetryDelay = TimeSpan.FromMilliseconds(500);

    // The database and its journal files.
    private static readonly string[] ClearedFiles = [DatabaseFileName, DatabaseFileName + "-wal", DatabaseFileName + "-shm", DatabaseFileName + "-journal"];

    // The icon cache (rebuilt on demand).
    private static readonly string[] ClearedFolders = ["icons"];

    private readonly LocalDatabase _database;
    private readonly SessionController _sessions;
    private readonly string _dataDirectory;

    /// <param name="dataDirectory">The folder the database lives in (<c>AccountabiliBuddy</c> or <c>AccountabiliBuddy-Dev</c> under Local AppData, or the test override).</param>
    public StartOverService(LocalDatabase database, SessionController sessions, string dataDirectory)
    {
        _database = database;
        _sessions = sessions;
        _dataDirectory = dataDirectory;
    }

    /// <summary>Why starting over is refused right now; null when it isn't.</summary>
    public string? BlockedReason => _sessions.IsEnforcing ? LockedReason : null;

    /// <summary>True when <paramref name="typed"/> is the confirmation phrase (case and outer spaces don't matter).</summary>
    public static bool IsConfirmed(string? typed) =>
        string.Equals(typed?.Trim(), ConfirmPhrase, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Copies the database and leaves the marker that makes the next start clear the data. The caller restarts the app straight after.
    /// </summary>
    /// <returns>Null when ready to restart; otherwise why not (nothing changed).</returns>
    public async Task<string?> PrepareAsync(string typed)
    {
        if (BlockedReason is string blocked)
        {
            return blocked;
        }

        if (!IsConfirmed(typed))
        {
            return $"Type \"{ConfirmPhrase}\" to confirm.";
        }

        try
        {
            string made = await _database.BackupAsync("start-over");
            // Renamed out of the "before-*.db" pattern, so the later copies before migrations and updates (which keep only the newest
            // ten of that pattern) never prune it.
            string copy = Path.Combine(Path.GetDirectoryName(made)!, "start-over-" + Path.GetFileName(made)["before-start-over-".Length..]);
            File.Move(made, copy, overwrite: true);
            // Only the newest few of these copies are kept (each is a whole database).
            foreach (FileInfo old in new DirectoryInfo(Path.GetDirectoryName(copy)!).GetFiles("start-over-*.db")
                         .OrderByDescending(file => file.CreationTimeUtc).Skip(KeptCopies))
            {
                old.Delete();
            }

            var marker = new Marker(TimeProvider.System.GetUtcNow(), copy);
            await File.WriteAllTextAsync(Path.Combine(_dataDirectory, MarkerFileName), JsonSerializer.Serialize(marker));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            return "Couldn't make the safety copy of your data, so nothing was cleared: " + ex.Message;
        }
    }

    /// <summary>
    /// Takes the marker away: the restart after <see cref="PrepareAsync"/> did not happen, so nothing may clear the data later.
    /// </summary>
    public void CancelPending()
    {
        TryDelete(Path.Combine(_dataDirectory, MarkerFileName));
    }

    /// <summary>
    /// Clears the data if the last run asked for it (see the class summary). Call at start, before the database is opened.
    /// </summary>
    /// <remarks>The marker is dated with the system clock, not the trusted clock: it only bridges one restart, and the trusted clock is not restored yet at this point.</remarks>
    /// <param name="problem">Set when the data was to be cleared and could not be (the old copy of the app still holds the database); nothing was touched then.</param>
    /// <returns>True when the data was cleared.</returns>
    public static bool RunPending(string dataDirectory, out string? problem)
    {
        problem = null;
        string markerPath = Path.Combine(dataDirectory, MarkerFileName);
        if (!File.Exists(markerPath))
        {
            return false;
        }

        Marker? marker = null;
        try
        {
            marker = JsonSerializer.Deserialize<Marker>(File.ReadAllText(markerPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Treated as no marker below.
        }

        // The marker is spent either way: it never waits for a later start.
        TryDelete(markerPath);
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        bool valid = marker is { BackupPath: not null }
            && now - marker.At >= TimeSpan.FromMinutes(-1)
            && now - marker.At <= MarkerLifetime
            && File.Exists(marker.BackupPath)
            && IsInside(Path.Combine(dataDirectory, "backups"), marker.BackupPath)
            && File.Exists(Path.Combine(dataDirectory, DatabaseFileName));
        if (!valid)
        {
            return false;
        }

        // The database first: if it can't be deleted (the old copy of the app can still be shutting down), it is tried for a few
        // seconds; if it still can't, nothing else is touched and the user is told.
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Delete(Path.Combine(dataDirectory, DatabaseFileName));
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= DeleteAttempts)
                {
                    problem = "Start over could not clear your data because the database was still in use. Nothing was changed; try again.";
                    return false;
                }

                Thread.Sleep(DeleteRetryDelay);
            }
        }

        foreach (string name in ClearedFiles.Skip(1))
        {
            TryDelete(Path.Combine(dataDirectory, name));
        }

        foreach (string name in ClearedFolders)
        {
            string folder = Path.Combine(dataDirectory, name);
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A cache; left for the app to overwrite.
            }
        }

        return true;
    }

    private static bool IsInside(string folder, string path)
    {
        string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing more to do; the next start looks again.
        }
    }

    private sealed record Marker(DateTimeOffset At, string BackupPath);
}
