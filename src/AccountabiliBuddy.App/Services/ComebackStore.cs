using System.Text.Json;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Come-backs (redesign phase 2, capture at the source): ideas parked with Ctrl+Alt+N or the work-hours gate, listed on
/// Today until ticked. Kept on this computer only, as one DPAPI-encrypted list (documents "capture.comebacks"), like a
/// session's checklist; reaching the phone waits for phase 5's ADR. Use from the UI thread only.
/// </summary>
public sealed class ComebackStore
{
    private const string DocumentKey = "capture.comebacks";
    private const int KeepDoneDays = 90;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SqliteJsonDocumentStore _store;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _time;
    private List<Comeback> _all = [];

    public ComebackStore(SqliteJsonDocumentStore store, IDataProtector protector, TimeProvider time)
    {
        _store = store;
        _protector = protector;
        _time = time;
    }

    /// <summary>Every come-back kept, open and ticked, oldest first.</summary>
    public IReadOnlyList<Comeback> All => _all;

    /// <summary>The ones not ticked yet, oldest first.</summary>
    public IReadOnlyList<Comeback> Open => _all.Where(comeback => comeback.DoneAt is null).ToList();

    public event EventHandler? Changed;

    public void LoadBlocking()
    {
        try
        {
            Stored? stored = Task.Run(() => _store.LoadAsync<Stored>(DocumentKey)).GetAwaiter().GetResult();
            string? json = stored is null ? null : _protector.Unprotect(Convert.FromBase64String(stored.Data));
            _all = json is null ? [] : JsonSerializer.Deserialize<List<Comeback>>(json, JsonOptions) ?? [];
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or FormatException)
        {
            _all = [];
        }
    }

    /// <summary>Adds one; a blank line adds nothing.</summary>
    public Task AddAsync(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return Task.CompletedTask;
        }

        _all.Add(new Comeback(Guid.NewGuid(), trimmed, _time.GetUtcNow()));
        return SaveAsync();
    }

    /// <summary>Ticks one: it leaves the list, keeping when (ticked ones are dropped after 90 days).</summary>
    public Task TickAsync(Guid id)
    {
        int index = _all.FindIndex(comeback => comeback.Id == id);
        if (index < 0 || _all[index].DoneAt is not null)
        {
            return Task.CompletedTask;
        }

        _all[index] = _all[index] with { DoneAt = _time.GetUtcNow() };
        return SaveAsync();
    }

    private Task SaveAsync()
    {
        DateTimeOffset cutoff = _time.GetUtcNow().AddDays(-KeepDoneDays);
        _all = _all.Where(comeback => comeback.DoneAt is null || comeback.DoneAt > cutoff).ToList();
        Changed?.Invoke(this, EventArgs.Empty);
        string sealedList = Convert.ToBase64String(_protector.Protect(JsonSerializer.Serialize(_all, JsonOptions)));
        return _store.SaveAsync(DocumentKey, new Stored(sealedList));
    }

    private sealed record Stored(string Data);
}
