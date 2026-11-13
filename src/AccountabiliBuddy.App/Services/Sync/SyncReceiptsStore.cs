using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// The receiver-local record behind this PC's receipts (ADR 86, <see cref="SyncReceiptsLocal"/>): per sender, by which method its newest
/// file arrived, why a read failed, what each part of its last file came to, which goal history months were merged. Kept in the device-local
/// document "sync.receipts.local" (a "sync." key: never in a backup, never sent as it is; the file carries only
/// <see cref="ForFile"/>). Recording never raises <see cref="SyncCoordinator.OwnDataChanged"/>: receiving a file must not trigger a write,
/// so receipts leave with the next write. Ids, times and codes only. Use from the UI thread.
/// </summary>
public sealed class SyncReceiptsStore
{
    /// <summary>The document key.</summary>
    public const string DocumentKey = "sync.receipts.local";

    private readonly SqliteJsonDocumentStore _store;
    private SyncReceiptsLocal _local = SyncReceiptsLocal.Empty;

    // Saves run one after another so an older record never overwrites a newer one.
    private Task _saving = Task.CompletedTask;

    public SyncReceiptsStore(SqliteJsonDocumentStore store)
    {
        _store = store;
    }

    /// <summary>The record as it is now.</summary>
    public SyncReceiptsLocal Value => _local;

    /// <summary>Raised (on the caller's thread) when the record changed, so a shown board can refresh.</summary>
    public event EventHandler? Changed;

    public void LoadBlocking()
    {
        try
        {
            List<SyncReceipt>? rows = Task.Run(() => _store.LoadAsync<List<SyncReceipt>>(DocumentKey)).GetAwaiter().GetResult();
            _local = new SyncReceiptsLocal(SyncReceiptParts.Clean(rows)?.ToList() ?? []);
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            _local = SyncReceiptsLocal.Empty;
        }
    }

    /// <inheritdoc cref="SyncReceiptsLocal.RecordArrival"/>
    public void RecordArrival(Guid from, string method, DateTimeOffset writtenAt) => Apply(_local.RecordArrival(from, method, writtenAt));

    /// <inheritdoc cref="SyncReceiptsLocal.RecordFailure"/>
    public void RecordFailure(Guid from, string method, string reason, DateTimeOffset at) => Apply(_local.RecordFailure(from, method, reason, at));

    /// <inheritdoc cref="SyncReceiptsLocal.RecordParts"/>
    public void RecordParts(Guid from, DateTimeOffset at, IReadOnlyDictionary<string, string> states) => Apply(_local.RecordParts(from, at, states));

    /// <inheritdoc cref="SyncReceiptsLocal.RecordHistory"/>
    public void RecordHistory(Guid from, string month, DateTimeOffset version) => Apply(_local.RecordHistory(from, month, version));

    /// <summary>Forgets the rows of devices that are gone (removed, ADR 54), and the whole record when <paramref name="all"/> (left the group).</summary>
    public void Forget(IReadOnlySet<Guid> ids, bool all = false) => Apply(all ? SyncReceiptsLocal.Empty : _local.Forget(ids));

    /// <summary>The list this PC writes into its file: null when it has no other active device (works alone), an empty list when it holds nothing yet.</summary>
    public IReadOnlyList<SyncReceipt>? ForFile(Guid self, IReadOnlySet<Guid> active) => _local.ForFile(self, active);

    /// <summary>The sender's row, if any.</summary>
    public SyncReceipt? Row(Guid from) => _local.Senders.FirstOrDefault(row => row.From == from);

    private void Apply(SyncReceiptsLocal next)
    {
        if (ReferenceEquals(next, _local) || System.Text.Json.JsonSerializer.Serialize(next.Senders, SyncJson.Options) == System.Text.Json.JsonSerializer.Serialize(_local.Senders, SyncJson.Options))
        {
            return;
        }

        _local = next;
        IReadOnlyList<SyncReceipt> toSave = next.Senders;
        _saving = _saving.ContinueWith(
            async _ =>
            {
                try
                {
                    await _store.SaveAsync(DocumentKey, toSave).ConfigureAwait(false);
                }
                catch (SqliteException)
                {
                    // Kept in memory; the next change saves again.
                }
            },
            TaskScheduler.Default).Unwrap();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
