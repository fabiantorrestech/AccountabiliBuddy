using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// App and site time the user deleted from History (documents "history.deleted"). The recorded activity is kept,
/// so a deletion can be restored, but deleted time counts nowhere: not in focus totals, top distractions or most
/// used. Use from the UI thread only.
/// </summary>
public sealed class DeletedUsageService
{
    private const string DocumentKey = "history.deleted";

    private readonly SqliteJsonDocumentStore _store;
    private List<DeletedUsage> _deleted = new();

    public DeletedUsageService(SqliteJsonDocumentStore store)
    {
        _store = store;
    }

    public event EventHandler? Changed;

    public IReadOnlyList<DeletedUsage> All => _deleted;

    /// <summary>Every deleted stretch, for leaving it out of History's totals.</summary>
    public IReadOnlyList<UsageDeletion> Parts => _deleted.SelectMany(deletion => deletion.Parts).ToList();

    public void LoadBlocking()
    {
        try
        {
            _deleted = Task.Run(() => _store.LoadAsync<List<DeletedUsage>>(DocumentKey)).GetAwaiter().GetResult() ?? new List<DeletedUsage>();
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            _deleted = new List<DeletedUsage>();
        }
    }

    /// <summary>Deletions that overlap the period shown.</summary>
    public IReadOnlyList<DeletedUsage> Overlapping(DateTimeOffset from, DateTimeOffset to)
    {
        return _deleted.Where(deletion => deletion.From < to && deletion.To > from).ToList();
    }

    public Task DeleteAsync(DeletedUsage deletion)
    {
        _deleted.Add(deletion);
        return SaveAsync();
    }

    public Task RestoreAsync(Guid id)
    {
        _deleted.RemoveAll(deletion => deletion.Id == id);
        return SaveAsync();
    }

    private async Task SaveAsync()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        await _store.SaveAsync(DocumentKey, _deleted);
    }
}
