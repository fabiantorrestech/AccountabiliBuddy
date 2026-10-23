using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Distracted stretches the user marked on past sessions in History (app-owned document "history.gaps", by session
/// start). The session log itself is never changed, so removing a stretch gives the time back exactly.
/// </summary>
public sealed class SessionGapsService
{
    private const string DocumentKey = "history.gaps";

    private readonly SqliteJsonDocumentStore _store;
    private Dictionary<DateTimeOffset, IReadOnlyList<SessionGap>> _gaps = new();

    public SessionGapsService(SqliteJsonDocumentStore store)
    {
        _store = store;
    }

    /// <summary>Raised after the gaps of a session changed (the day bar, History and linked devices read again).</summary>
    public event EventHandler? Changed;

    public void LoadBlocking()
    {
        try
        {
            List<StoredGaps>? saved = Task.Run(() => _store.LoadAsync<List<StoredGaps>>(DocumentKey)).GetAwaiter().GetResult();
            _gaps = (saved ?? []).Where(entry => entry.Gaps is { Count: > 0 }).ToDictionary(entry => entry.Session, entry => (IReadOnlyList<SessionGap>)entry.Gaps);
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            _gaps = new();
        }
    }

    public IReadOnlyList<SessionGap> GapsOf(DateTimeOffset sessionStart) =>
        _gaps.TryGetValue(sessionStart, out IReadOnlyList<SessionGap>? gaps) ? gaps : [];

    /// <summary>The records with their marked stretches folded in as away time of kind Distracted.</summary>
    public IReadOnlyList<SessionRecord> Apply(IReadOnlyList<SessionRecord> records) => SessionGaps.Apply(records, GapsOf);

    /// <summary>Replaces the session's stretches (none = removes them). Check them first with <see cref="SessionGaps.Validate"/>.</summary>
    public async Task SaveAsync(DateTimeOffset sessionStart, IReadOnlyList<SessionGap> gaps)
    {
        if (gaps.Count == 0)
        {
            _gaps.Remove(sessionStart);
        }
        else
        {
            _gaps[sessionStart] = gaps.OrderBy(gap => gap.From).ToList();
        }

        await _store.SaveAsync(DocumentKey, _gaps.Select(entry => new StoredGaps(entry.Key, entry.Value.ToList())).ToList());
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed record StoredGaps(DateTimeOffset Session, List<SessionGap> Gaps);
}
