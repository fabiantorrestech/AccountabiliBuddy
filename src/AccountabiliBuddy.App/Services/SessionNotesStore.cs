using System.Text.Json;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Notes (redesign phase 5, capture): lines typed in Focus mode that may drift; shown once on the session's review card
/// and kept with the session in History, matched by time. One DPAPI-encrypted list on this computer (documents
/// "capture.notes"), kept 90 days; never sent anywhere. Use from the UI thread only.
/// </summary>
public sealed class SessionNotesStore
{
    private const string DocumentKey = "capture.notes";
    private const int KeepDays = 90;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SqliteJsonDocumentStore _store;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _time;
    private List<SessionNote> _all = [];

    // Saves run one after another, so an older list never overwrites a newer one.
    private Task _saving = Task.CompletedTask;

    public SessionNotesStore(SqliteJsonDocumentStore store, IDataProtector protector, TimeProvider time)
    {
        _store = store;
        _protector = protector;
        _time = time;
    }

    /// <summary>Completes when every save asked for so far is written.</summary>
    public Task WhenSaved => _saving;

    /// <summary>Every note kept, oldest first.</summary>
    public IReadOnlyList<SessionNote> All => _all;

    public event EventHandler? Changed;

    public void LoadBlocking()
    {
        try
        {
            Stored? stored = Task.Run(() => _store.LoadAsync<Stored>(DocumentKey)).GetAwaiter().GetResult();
            string? json = stored is null ? null : _protector.Unprotect(Convert.FromBase64String(stored.Data));
            _all = json is null ? [] : JsonSerializer.Deserialize<List<SessionNote>>(json, JsonOptions) ?? [];
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or FormatException)
        {
            _all = [];
        }
    }

    /// <summary>Keeps one, timed now; a blank line keeps nothing.</summary>
    public Task AddAsync(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return Task.CompletedTask;
        }

        _all.Add(new SessionNote(Guid.NewGuid(), trimmed, _time.GetUtcNow()));
        return SaveAsync();
    }

    /// <summary>The notes' texts written from <paramref name="from"/> to <paramref name="to"/>, oldest first.</summary>
    public IReadOnlyList<string> Between(DateTimeOffset from, DateTimeOffset to)
    {
        return _all.Where(note => note.At >= from && note.At <= to).Select(note => note.Text).ToList();
    }

    private Task SaveAsync()
    {
        DateTimeOffset cutoff = _time.GetUtcNow().AddDays(-KeepDays);
        _all = _all.Where(note => note.At > cutoff).ToList();
        Changed?.Invoke(this, EventArgs.Empty);
        string sealedList = Convert.ToBase64String(_protector.Protect(JsonSerializer.Serialize(_all, JsonOptions)));
        _saving = _saving.ContinueWith(
            async _ =>
            {
                try
                {
                    await _store.SaveAsync(DocumentKey, new Stored(sealedList));
                }
                catch (SqliteException)
                {
                    // Best effort: the note stays in memory until the next save.
                }
            },
            TaskScheduler.Default).Unwrap();
        return _saving;
    }

    private sealed record Stored(string Data);
}
