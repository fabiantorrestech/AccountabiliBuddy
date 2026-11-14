using System.Text.Json;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Notes (redesign phase 5, capture): lines typed in Focus mode that may drift; shown once on the session's review card
/// and kept with the session in History, and listed in the Archive. One DPAPI-encrypted list on this computer (documents
/// "capture.notes"), kept 90 days. Since ADR 87 the notes travel between the user's own devices, only inside the sealed sync files
/// and only while "Sync task lists" is on (<c>TaskListSync</c>): each note keeps the session it was written in (start and
/// focus profile id) and the device that wrote it, an edit stamp, and, once deleted, a tombstone with no text kept 30 days
/// (<see cref="SessionNoteRules"/>). The texts are never logged. Use from the UI thread only.
/// </summary>
public sealed class SessionNotesStore
{
    private const string DocumentKey = "capture.notes";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SqliteJsonDocumentStore _store;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _time;
    private List<SyncSessionNote> _all = [];

    // Saves run one after another, so an older list never overwrites a newer one.
    private Task _saving = Task.CompletedTask;

    public SessionNotesStore(SqliteJsonDocumentStore store, IDataProtector protector, TimeProvider time)
    {
        _store = store;
        _protector = protector;
        _time = time;
    }

    /// <summary>This device's id (the sync identity); null until it has one. Set by the app.</summary>
    public Func<Guid?> Self { get; set; } = () => null;

    /// <summary>The running session's start and focus profile id, null outside a session; stamped on each new note. Set by the app.</summary>
    public Func<(DateTimeOffset Start, Guid? ProfileId)?> CurrentSession { get; set; } = () => null;

    /// <summary>
    /// The session of this device that a note written at the given time belongs to (start and profile id), null for none; used once to give
    /// the notes stored before ADR 87 their session. Set by the app.
    /// </summary>
    public Func<DateTimeOffset, (DateTimeOffset Start, Guid? ProfileId)?> SessionOf { get; set; } = _ => null;

    /// <summary>Completes when every save asked for so far is written.</summary>
    public Task WhenSaved => _saving;

    /// <summary>Every live note kept (this device's and the other devices'), oldest first.</summary>
    public IReadOnlyList<SessionNote> All => _all
        .Where(note => note.DeletedAt is null && note.Text is not null)
        .OrderBy(note => note.At)
        .Select(note => new SessionNote(note.Id, note.Text!, note.At, note.By))
        .ToList();

    public event EventHandler? Changed;

    public void LoadBlocking()
    {
        try
        {
            Stored? stored = Task.Run(() => _store.LoadAsync<Stored>(DocumentKey)).GetAwaiter().GetResult();
            string? json = stored is null ? null : _protector.Unprotect(Convert.FromBase64String(stored.Data));
            _all = json is null ? [] : JsonSerializer.Deserialize<List<SyncSessionNote>>(json, JsonOptions) ?? [];
            // A note stored without an id gets one, so it can be edited or deleted; kept with the next save.
            _all = _all.Where(note => note is not null).Select(note => note.Id == Guid.Empty ? note with { Id = Guid.NewGuid() } : note).ToList();
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or FormatException)
        {
            _all = [];
        }
    }

    /// <summary>Keeps one, timed now and tied to the running session; a blank line keeps nothing.</summary>
    public Task AddAsync(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return Task.CompletedTask;
        }

        DateTimeOffset now = _time.GetUtcNow();
        (DateTimeOffset Start, Guid? ProfileId)? session = CurrentSession();
        _all.Add(new SyncSessionNote(Guid.NewGuid(), trimmed, now, session?.Start, session?.ProfileId, Self() ?? Guid.Empty, now));
        return SaveAsync();
    }

    /// <summary>The notes' texts this device wrote from <paramref name="from"/> to <paramref name="to"/>, oldest first (never another device's, ADR 87).</summary>
    public IReadOnlyList<string> Between(DateTimeOffset from, DateTimeOffset to)
    {
        return SessionNoteRules.Between(_all, Self() ?? Guid.Empty, from, to);
    }

    /// <summary>Changes one's text (the Notebook's edit), stamped as an edit; a blank text or an unknown id changes nothing.</summary>
    public Task EditAsync(Guid id, string text)
    {
        string trimmed = text.Trim();
        int index = _all.FindIndex(note => note.Id == id && note.DeletedAt is null);
        if (index < 0 || trimmed.Length == 0 || _all[index].Text == trimmed)
        {
            return Task.CompletedTask;
        }

        SyncSessionNote held = _all[index];
        _all[index] = held with { Text = trimmed, EditedAt = SharedListMerge.NextStamp(_time.GetUtcNow(), held) };
        return SaveAsync();
    }

    /// <summary>Deletes one (the Notebook's delete): its text is gone at once and a tombstone tells the other devices; an unknown id changes nothing.</summary>
    public Task DeleteAsync(Guid id)
    {
        int index = _all.FindIndex(note => note.Id == id && note.DeletedAt is null);
        if (index < 0)
        {
            return Task.CompletedTask;
        }

        SyncSessionNote held = _all[index];
        _all[index] = held.Deleted(SharedListMerge.NextStamp(_time.GetUtcNow(), held));
        return SaveAsync();
    }

    /// <summary>
    /// The notes for this device's file (ADR 87): live notes and tombstones within their windows. Notes stored before ADR 87 are stamped
    /// first (once: this device as the writer, an edit time of when it was written, the session found by <see cref="SessionOf"/>).
    /// Empty until this device has an id.
    /// </summary>
    public IReadOnlyList<SyncSessionNote> ForSync()
    {
        if (Self() is not Guid)
        {
            return [];
        }

        StampLegacy();
        return SessionNoteRules.ForFile(_all.Where(note => note.By != Guid.Empty), _time.GetUtcNow());
    }

    /// <summary>
    /// Takes in the notes of another device's file (ADR 87), merged item by item (<see cref="SessionNoteRules.Merge"/>): an edit or a
    /// deletion made there reaches this device, a note only here stays. Nothing is written and nothing is raised when the merge changes nothing.
    /// </summary>
    public Task MergeAsync(IEnumerable<SyncSessionNote> incoming)
    {
        if (Self() is not Guid)
        {
            return Task.CompletedTask;
        }

        StampLegacy();
        DateTimeOffset now = _time.GetUtcNow();
        List<SyncSessionNote> merged = SessionNoteRules.Merge(_all, SessionNoteRules.Clean(incoming), now);
        if (merged.SequenceEqual(_all))
        {
            return Task.CompletedTask;
        }

        _all = merged;
        return SaveAsync(prune: false);
    }

    // The one-time stamp for notes stored before ADR 87 (no writing device yet): this device, edited when written, in the own session
    // that was running then. Never repeated: a stamped note has a writing device.
    private void StampLegacy()
    {
        if (Self() is not Guid self || !_all.Any(note => note.By == Guid.Empty))
        {
            return;
        }

        _all = _all
            .Select(note =>
            {
                if (note.By != Guid.Empty)
                {
                    return note;
                }

                (DateTimeOffset Start, Guid? ProfileId)? session = note.DeletedAt is null ? SessionOf(note.At) : null;
                return note with { By = self, EditedAt = note.At, SessionStart = session?.Start, ProfileId = session?.ProfileId };
            })
            .ToList();
        _ = SaveAsync(prune: false, raise: false);
    }

    private Task SaveAsync(bool prune = true, bool raise = true)
    {
        // The window and the tombstones' 30 days are applied on every save (the merge applies them too).
        if (prune)
        {
            _all = SessionNoteRules.ForFile(_all, _time.GetUtcNow());
        }

        if (raise)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

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
