using System.Text.Json;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The "how did today go" answers of the shutdown routine: a feeling and a one-line note per plan date. Device-local state, kept as one
/// DPAPI-encrypted list (documents "shutdown.reviews"); never synced and never logged, since the note is private text. A review
/// goes for good 90 days after it was saved. Use from the UI thread only.
/// </summary>
public sealed class ShutdownReviewStore
{
    public const int MaxNoteLength = 200;

    private const string DocumentKey = "shutdown.reviews";
    private static readonly TimeSpan Kept = TimeSpan.FromDays(90);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SqliteJsonDocumentStore _store;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _time;
    private List<ShutdownReview> _all = [];

    // Saves run one after another, so an older list never overwrites a newer one.
    private Task _saving = Task.CompletedTask;

    public ShutdownReviewStore(SqliteJsonDocumentStore store, IDataProtector protector, TimeProvider time)
    {
        _store = store;
        _protector = protector;
        _time = time;
    }

    /// <summary>The reviews younger than 90 days, newest date first.</summary>
    public IReadOnlyList<ShutdownReview> Items
    {
        get
        {
            DateTimeOffset keptFrom = _time.GetUtcNow() - Kept;
            return _all.Where(review => review.SavedAt >= keptFrom).OrderByDescending(review => review.Date).ToList();
        }
    }

    public event EventHandler? Changed;

    public void LoadBlocking()
    {
        try
        {
            Stored? stored = Task.Run(() => _store.LoadAsync<Stored>(DocumentKey)).GetAwaiter().GetResult();
            string? json = stored is null ? null : _protector.Unprotect(Convert.FromBase64String(stored.Data));
            _all = json is null ? [] : JsonSerializer.Deserialize<List<ShutdownReview>>(json, JsonOptions) ?? [];
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or FormatException)
        {
            _all = [];
        }

        if (_all.Count != Items.Count)
        {
            // Reviews older than 90 days go for good.
            _ = SaveAsync();
        }
    }

    /// <summary>The review of a plan date, or null.</summary>
    public ShutdownReview? Of(DateOnly date)
    {
        return Items.FirstOrDefault(review => review.Date == date);
    }

    /// <summary>The reviews whose dates fall from <paramref name="from"/> to <paramref name="to"/>, oldest first.</summary>
    public IReadOnlyList<ShutdownReview> Between(DateOnly from, DateOnly to)
    {
        return Items.Where(review => review.Date >= from && review.Date <= to).OrderBy(review => review.Date).ToList();
    }

    /// <summary>
    /// Keeps the review of <paramref name="date"/>, replacing an earlier one. A feeling outside 1 to 5 counts as none; a blank note
    /// and no feeling keep nothing (and clear an earlier review of that date).
    /// </summary>
    public Task SaveReviewAsync(DateOnly date, int? feeling, string note)
    {
        int? kept = feeling is >= 1 and <= 5 ? feeling : null;
        string line = note.Trim().ReplaceLineEndings(" ");
        if (line.Length > MaxNoteLength)
        {
            line = line[..MaxNoteLength].TrimEnd();
        }

        _all.RemoveAll(review => review.Date == date);
        if (kept is not null || line.Length > 0)
        {
            _all.Add(new ShutdownReview(date, kept, line, _time.GetUtcNow()));
        }

        return SaveAsync();
    }

    private Task SaveAsync()
    {
        _all = Items.ToList();
        Changed?.Invoke(this, EventArgs.Empty);
        string sealedList = Convert.ToBase64String(_protector.Protect(JsonSerializer.Serialize(_all, JsonOptions)));
        _saving = _saving.ContinueWith(_ => _store.SaveAsync(DocumentKey, new Stored(sealedList)), TaskScheduler.Default).Unwrap();
        return _saving;
    }

    private sealed record Stored(string Data);
}
