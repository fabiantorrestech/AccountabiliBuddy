using System.Text.Json;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// ADR 72's handovers on this device: the items it is carrying to the device the user chose to keep them (until that device's
/// receipt shows), and its own receipts for the items handed to it (written for 30 days). Kept DPAPI-encrypted (document
/// "tasks.handovers"), never logged. A handover not taken in 30 days is given back through <see cref="Expired"/>. Use from the UI
/// thread only.
/// </summary>
public sealed class HandoverOutbox
{
    private const string DocumentKey = "tasks.handovers";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SqliteJsonDocumentStore _store;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _time;
    private State _state = new([], []);
    private Task _saving = Task.CompletedTask;

    public HandoverOutbox(SqliteJsonDocumentStore store, IDataProtector protector, TimeProvider time)
    {
        _store = store;
        _protector = protector;
        _time = time;
    }

    /// <summary>A handover nobody took in 30 days: the caller takes the item back, local here, and tells the user.</summary>
    public event EventHandler<SyncHandover>? Expired;

    public event EventHandler? Changed;

    /// <summary>The handovers still on their way.</summary>
    public IReadOnlyList<SyncHandover> Outgoing => _state.Outgoing;

    public void LoadBlocking()
    {
        try
        {
            Stored? stored = Task.Run(() => _store.LoadAsync<Stored>(DocumentKey)).GetAwaiter().GetResult();
            string? json = stored is null ? null : _protector.Unprotect(Convert.FromBase64String(stored.Data));
            _state = json is null ? new State([], []) : JsonSerializer.Deserialize<State>(json, JsonOptions) ?? new State([], []);
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or FormatException)
        {
            _state = new State([], []);
        }
    }

    /// <summary>Carries <paramref name="handover"/> from now on; an older handover of the same item is replaced.</summary>
    public Task SendAsync(SyncHandover handover)
    {
        _state = _state with { Outgoing = _state.Outgoing.Where(held => held.Id != handover.Id).Append(handover).ToList() };
        return SaveAsync();
    }

    /// <summary>The handovers for the file, after giving back the ones past 30 days.</summary>
    public IReadOnlyList<SyncHandover> ForSync()
    {
        DateTimeOffset now = _time.GetUtcNow();
        List<SyncHandover> expired = _state.Outgoing.Where(handover => now - handover.SentAt >= SyncHandover.Kept).ToList();
        if (expired.Count > 0)
        {
            _state = _state with { Outgoing = _state.Outgoing.Except(expired).ToList() };
            _ = SaveAsync();
            foreach (SyncHandover handover in expired)
            {
                Expired?.Invoke(this, handover);
            }
        }

        return _state.Outgoing;
    }

    /// <summary>This device's receipts for the file (30 days).</summary>
    public IReadOnlyList<SyncHandoverAck> AcksForSync()
    {
        DateTimeOffset from = _time.GetUtcNow() - SyncHandover.Kept;
        return _state.Receipts.Where(receipt => receipt.TakenAt >= from).Select(receipt => receipt.Ack).ToList();
    }

    /// <summary>The handovers in another device's file addressed to <paramref name="self"/> that weren't taken in yet.</summary>
    public IReadOnlyList<SyncHandover> NewFor(Guid self, IReadOnlyList<SyncHandover>? incoming)
    {
        return (incoming ?? [])
            .Where(handover => handover is not null && handover.To == self)
            .Where(handover => !_state.Receipts.Any(receipt => receipt.Ack.Id == handover.Id && receipt.Ack.EditedAt >= handover.EditedAt))
            .ToList();
    }

    /// <summary>Records that <paramref name="handover"/> was taken in here: its receipt goes out with this device's files.</summary>
    public Task TakenAsync(SyncHandover handover)
    {
        var receipt = new Receipt(new SyncHandoverAck(handover.Id, handover.EditedAt), _time.GetUtcNow());
        _state = _state with { Receipts = _state.Receipts.Where(held => held.Ack.Id != handover.Id).Append(receipt).ToList() };
        return SaveAsync();
    }

    /// <summary>Stops carrying the handovers <paramref name="keeper"/> has receipted in its file.</summary>
    public Task AcknowledgedAsync(Guid keeper, IReadOnlyList<SyncHandoverAck>? acks)
    {
        if (acks is not { Count: > 0 })
        {
            return Task.CompletedTask;
        }

        List<SyncHandover> done = _state.Outgoing
            .Where(handover => handover.To == keeper && acks.Any(ack => ack is not null && ack.Id == handover.Id && ack.EditedAt >= handover.EditedAt))
            .ToList();
        if (done.Count == 0)
        {
            return Task.CompletedTask;
        }

        _state = _state with { Outgoing = _state.Outgoing.Except(done).ToList() };
        return SaveAsync();
    }

    private Task SaveAsync()
    {
        DateTimeOffset from = _time.GetUtcNow() - SyncHandover.Kept;
        _state = _state with { Receipts = _state.Receipts.Where(receipt => receipt.TakenAt >= from).ToList() };
        Changed?.Invoke(this, EventArgs.Empty);
        string sealedState = Convert.ToBase64String(_protector.Protect(JsonSerializer.Serialize(_state, JsonOptions)));
        _saving = _saving.ContinueWith(_ => _store.SaveAsync(DocumentKey, new Stored(sealedState)), TaskScheduler.Default).Unwrap();
        return _saving;
    }

    private sealed record Stored(string Data);

    private sealed record Receipt(SyncHandoverAck Ack, DateTimeOffset TakenAt);

    private sealed record State(IReadOnlyList<SyncHandover> Outgoing, IReadOnlyList<Receipt> Receipts);
}
