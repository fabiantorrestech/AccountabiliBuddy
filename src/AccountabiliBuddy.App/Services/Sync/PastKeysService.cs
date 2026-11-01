using System.Text.Json;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// The group's past keys (ADR 56): on every key switch (<see cref="SyncIdentityService.KeySwitched"/>) the outgoing group id and AES
/// key are added to a local list, so a removed device's backup, sealed with the key of its day, can still be opened after later
/// removals. The list holds the derived AES key, never the link code, and is stored DPAPI-protected as one document ("sync.pastKeys").
/// It keeps the 20 newest keys, none older than a year. Devices share it in their sealed sync files
/// (<see cref="SyncFile.PastKeys"/>, merged as a set by group id through <see cref="MergeAsync"/>), so one that joins later learns
/// it too. A past key is used for one thing: opening a backup whose header names its group (<see cref="KeyFor"/>); never to read
/// or write a sync file. Use from the UI thread only.
/// </summary>
public sealed class PastKeysService
{
    private const string DocumentKey = "sync.pastKeys";
    private const int AesKeyBytes = 32;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private readonly SqliteJsonDocumentStore _store;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _saving = new(1, 1);

    private List<SyncPastKey> _keys = [];

    public PastKeysService(SqliteJsonDocumentStore store, IDataProtector protector, TimeProvider time)
    {
        _store = store;
        _protector = protector;
        _time = time;
    }

    /// <summary>Loads the list; one that can't be unprotected (copied from another user or PC) or read is empty.</summary>
    public void LoadBlocking()
    {
        try
        {
            StoredPastKeys? stored = Task.Run(() => _store.LoadAsync<StoredPastKeys>(DocumentKey)).GetAwaiter().GetResult();
            string? json = stored?.Protected is { } text ? _protector.Unprotect(Convert.FromBase64String(text)) : null;
            _keys = json is null ? [] : Prune(Valid(JsonSerializer.Deserialize<List<SyncPastKey>>(json, Options) ?? []));
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or FormatException)
        {
            _keys = [];
        }
    }

    /// <summary>The keys to write into this PC's sealed sync file; null when there are none.</summary>
    public IReadOnlyList<SyncPastKey>? ForFile()
    {
        _keys = Prune(_keys);
        return _keys.Count == 0 ? null : _keys.ToList();
    }

    /// <returns>The key that opens files of the group <paramref name="groupId"/> (8 lower-case hex characters), for a backup only; null when none is kept.</returns>
    public SyncKey? KeyFor(string groupId)
    {
        SyncPastKey? found = _keys.FirstOrDefault(key => key.GroupId == groupId);
        return found is null ? null : SyncKey.ForOpeningOnly(Convert.FromBase64String(found.Key), found.GroupId);
    }

    /// <summary>The group left <paramref name="outgoing"/> (a rekey applied or made): keeps it. Saves in the background.</summary>
    public void NoteSwitched(SyncKey outgoing)
    {
        var entry = new SyncPastKey(outgoing.GroupId, Convert.ToBase64String(outgoing.Key), _time.GetUtcNow());
        _keys = Prune(_keys.Where(key => key.GroupId != entry.GroupId).Append(entry).ToList());
        _ = SaveQuietlyAsync();
    }

    /// <summary>
    /// Takes in the keys another device wrote in a sealed file that opened with the group's current key: a set union by group id (a
    /// key held already wins), checked for shape, capped and pruned like the local ones.
    /// </summary>
    public async Task MergeAsync(IEnumerable<SyncPastKey>? received)
    {
        if (received is null)
        {
            return;
        }

        List<SyncPastKey> incoming = Valid(received.Take(SyncPastKey.MaxKept * 2).OfType<SyncPastKey>().ToList());
        List<SyncPastKey> merged = _keys.Concat(incoming.Where(key => _keys.All(held => held.GroupId != key.GroupId))).ToList();
        merged = Prune(merged);
        if (merged.Count == _keys.Count && merged.All(key => _keys.Any(held => held.GroupId == key.GroupId)))
        {
            return;
        }

        _keys = merged;
        await SaveAsync();
    }

    // Shape only: a group id of 8 hex characters and a 32-byte key; a time in the future counts as now.
    private List<SyncPastKey> Valid(IEnumerable<SyncPastKey> keys)
    {
        DateTimeOffset now = _time.GetUtcNow();
        var result = new List<SyncPastKey>();
        foreach (SyncPastKey key in keys)
        {
            if (key?.GroupId is not { Length: 8 } groupId || key.Key is null || !groupId.All(Uri.IsHexDigit) || !HasKeyLength(key.Key))
            {
                continue;
            }

            result.Add(new SyncPastKey(groupId.ToLowerInvariant(), key.Key, key.RetiredAt > now ? now : key.RetiredAt));
        }

        return result;
    }

    private static bool HasKeyLength(string base64)
    {
        try
        {
            return Convert.FromBase64String(base64).Length == AesKeyBytes;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    // None older than a year, one per group id, the 20 newest.
    private List<SyncPastKey> Prune(List<SyncPastKey> keys)
    {
        DateTimeOffset oldest = _time.GetUtcNow() - SyncPastKey.KeptFor;
        return keys
            .Where(key => key.RetiredAt >= oldest)
            .GroupBy(key => key.GroupId)
            .Select(group => group.OrderByDescending(key => key.RetiredAt).First())
            .OrderByDescending(key => key.RetiredAt)
            .Take(SyncPastKey.MaxKept)
            .ToList();
    }

    private async Task SaveQuietlyAsync()
    {
        try
        {
            await SaveAsync();
        }
        catch (SqliteException)
        {
            // Kept in memory for this run; the next change saves it again.
        }
    }

    private async Task SaveAsync()
    {
        await _saving.WaitAsync();
        try
        {
            string protectedList = Convert.ToBase64String(_protector.Protect(JsonSerializer.Serialize(_keys, Options)));
            await _store.SaveAsync(DocumentKey, new StoredPastKeys(protectedList));
        }
        finally
        {
            _saving.Release();
        }
    }

    /// <param name="Protected">The list as JSON, DPAPI-protected, as base64.</param>
    private sealed record StoredPastKeys(string? Protected);
}
