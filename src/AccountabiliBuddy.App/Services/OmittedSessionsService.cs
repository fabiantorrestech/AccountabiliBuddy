using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Sessions the user omitted (tests, trying things out), by their start time (documents
/// "history.notReal"). They stay in the history list but don't count: not on the day bar or timeline, not in focus
/// totals. Display only; the session events themselves are never changed. Use from the UI thread only.
/// </summary>
public sealed class OmittedSessionsService
{
    // Named before the feature was called "omit"; kept so earlier marks still count.
    private const string DocumentKey = "history.notReal";

    private readonly SqliteJsonDocumentStore _store;
    private HashSet<DateTimeOffset> _starts = new();

    public OmittedSessionsService(SqliteJsonDocumentStore store)
    {
        _store = store;
    }

    public event EventHandler? Changed;

    public void LoadBlocking()
    {
        try
        {
            List<DateTimeOffset>? saved = Task.Run(() => _store.LoadAsync<List<DateTimeOffset>>(DocumentKey)).GetAwaiter().GetResult();
            _starts = saved is null ? new HashSet<DateTimeOffset>() : new HashSet<DateTimeOffset>(saved);
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            _starts = new HashSet<DateTimeOffset>();
        }
    }

    /// <summary>True when the session that started at <paramref name="startedAt"/> was omitted.</summary>
    public bool IsOmitted(DateTimeOffset startedAt)
    {
        return _starts.Contains(startedAt);
    }

    public async Task SetOmittedAsync(DateTimeOffset startedAt, bool omitted)
    {
        bool changed = omitted ? _starts.Add(startedAt) : _starts.Remove(startedAt);
        if (!changed)
        {
            return;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        await _store.SaveAsync(DocumentKey, _starts.OrderBy(start => start).ToList());
    }
}
