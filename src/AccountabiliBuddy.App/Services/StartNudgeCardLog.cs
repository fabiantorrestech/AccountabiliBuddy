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

    // Adds and prunes run one after another (each reads, then writes the list).
    private Task _turn = Task.CompletedTask;

    public StartNudgeCardLog(SqliteJsonDocumentStore store, IDataProtector protector)
    {
        _store = store;
        _protector = protector;
    }

    /// <summary>Adds one reason and drops the ones older than <see cref="KeepFor"/>.</summary>
    public Task AddAsync(DateTimeOffset at, string reason)
    {
        var skip = new StartNudgeCardSkip(at, Convert.ToBase64String(_protector.Protect(reason.Trim())));
        return InTurnAsync(async () =>
        {
            IReadOnlyList<StartNudgeCardSkip> kept = await LoadAsync();
            List<StartNudgeCardSkip> next = [.. kept.Where(entry => at - entry.At < KeepFor), skip];
            await _store.SaveAsync(DocumentKey, next);
        });
    }

    /// <summary>Drops the reasons older than <see cref="KeepFor"/>; writes only when something went.</summary>
    public Task PruneAsync(DateTimeOffset now)
    {
        return InTurnAsync(async () =>
        {
            IReadOnlyList<StartNudgeCardSkip> kept = await LoadAsync();
            List<StartNudgeCardSkip> next = kept.Where(entry => now - entry.At < KeepFor).ToList();
            if (next.Count < kept.Count)
            {
                await _store.SaveAsync(DocumentKey, next);
            }
        });
    }

    /// <summary>
    /// Runs one read-and-write after the ones asked for before it, so an add and a prune never write over each
    /// other; a failed write is let go (best effort: the next add or prune writes again).
    /// </summary>
    private Task InTurnAsync(Func<Task> work)
    {
        _turn = _turn.ContinueWith(
            async _ =>
            {
                try
                {
                    await work();
                }
                catch (SqliteException)
                {
                    // Best effort, like the reason it would have kept.
                }
            },
            TaskScheduler.Default).Unwrap();
        return _turn;
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
