using System.Text.Json;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// Loads and saves <see cref="GoalHistoryState"/> in the documents table, sealed with this PC's key (DPAPI): it holds goal entries (as the
/// last written month files) and device ids. Saves run one after another so an older state never overwrites a newer one. A document that
/// can't be opened is started again empty: the history files are read and written again, which is safe (every merge is a union).
/// </summary>
public sealed class GoalHistoryStateStore
{
    public const string Key = "goals.history.sync";

    private readonly SqliteJsonDocumentStore _documents;
    private readonly IDataProtector _protector;
    private Task _saving = Task.CompletedTask;

    public GoalHistoryStateStore(SqliteJsonDocumentStore documents, IDataProtector protector)
    {
        _documents = documents;
        _protector = protector;
    }

    public GoalHistoryState Value { get; private set; } = new();

    public void LoadBlocking()
    {
        Value = Task.Run(ReadAsync).GetAwaiter().GetResult();
    }

    /// <summary>Saves the state as it is now (after the caller changed <see cref="Value"/>).</summary>
    public Task SaveAsync()
    {
        string json = JsonSerializer.Serialize(Value, SyncJson.Options);
        Task previous = _saving;
        Task next = Task.Run(async () =>
        {
            try
            {
                await previous;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // An earlier failed save does not stop this one.
            }

            await _documents.SaveAsync(Key, new Stored(Convert.ToBase64String(_protector.Protect(json))));
        });
        _saving = next;
        return next;
    }

    /// <summary>Drops everything (the group's key changed).</summary>
    public void Reset()
    {
        Value = new GoalHistoryState();
    }

    private async Task<GoalHistoryState> ReadAsync()
    {
        try
        {
            Stored? stored = await _documents.LoadAsync<Stored>(Key);
            if (stored is null || string.IsNullOrEmpty(stored.Data))
            {
                return new GoalHistoryState();
            }

            string? json = _protector.Unprotect(Convert.FromBase64String(stored.Data));
            return json is null ? new GoalHistoryState() : JsonSerializer.Deserialize<GoalHistoryState>(json, SyncJson.Options) ?? new GoalHistoryState();
        }
        catch (Exception ex) when (ex is JsonException or FormatException or NotSupportedException)
        {
            return new GoalHistoryState();
        }
    }

    private sealed record Stored(string Data);
}
