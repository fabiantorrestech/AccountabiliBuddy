using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The reasons given for "Not now" on the start-nudge card (document "startNudge.notNow"): each sealed with DPAPI, kept
/// <see cref="KeepFor"/>, never sent to another device.
/// </summary>
public sealed class StartNudgeCardLog
{
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(14);
    private const string DocumentKey = "startNudge.notNow";

    private readonly SqliteJsonDocumentStore _store;
    private readonly IDataProtector _protector;

    public StartNudgeCardLog(SqliteJsonDocumentStore store, IDataProtector protector)
    {
        _store = store;
        _protector = protector;
    }

    /// <summary>Adds one reason and drops the ones older than <see cref="KeepFor"/>.</summary>
    public async Task AddAsync(DateTimeOffset at, string reason)
    {
        IReadOnlyList<StartNudgeCardSkip> kept = await LoadAsync();
        var skip = new StartNudgeCardSkip(at, Convert.ToBase64String(_protector.Protect(reason.Trim())));
        List<StartNudgeCardSkip> next = [.. kept.Where(entry => at - entry.At < KeepFor), skip];
        await _store.SaveAsync(DocumentKey, next);
    }

    /// <summary>Drops the reasons older than <see cref="KeepFor"/>; writes only when something went.</summary>
    public async Task PruneAsync(DateTimeOffset now)
    {
        IReadOnlyList<StartNudgeCardSkip> kept = await LoadAsync();
        List<StartNudgeCardSkip> next = kept.Where(entry => now - entry.At < KeepFor).ToList();
        if (next.Count < kept.Count)
        {
            await _store.SaveAsync(DocumentKey, next);
        }
    }

    private async Task<IReadOnlyList<StartNudgeCardSkip>> LoadAsync()
    {
        try
        {
            return await _store.LoadAsync<List<StartNudgeCardSkip>>(DocumentKey) ?? [];
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            return [];
        }
    }
}
