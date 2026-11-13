namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// The receiver-local record behind a device's receipts (ADR 86; stored as the device-local document <c>sync.receipts.local</c>, never
/// sent as it is and never in a backup): one <see cref="SyncReceipt"/> row per sender this device has read a file from. Pure and
/// immutable: every operation returns a new record, and every dictionary it touches is rebuilt so its order is the order of first
/// insertion (the phone's twin writes the same JSON). The file's <c>receipts</c> list is <see cref="ForFile"/> of it.
/// </summary>
/// <param name="Senders">One row per sender, in the order first heard of.</param>
public sealed record SyncReceiptsLocal(IReadOnlyList<SyncReceipt> Senders)
{
    public static SyncReceiptsLocal Empty { get; } = new([]);

    /// <summary>
    /// A file of <paramref name="from"/> written at <paramref name="writtenAt"/> arrived by <paramref name="method"/> (recorded before any
    /// "already has newer data" decision, so a copy that came late still proves the method works): the method's newest time and the held
    /// time move forward only, and the method's earlier failure is cleared. An unknown method changes nothing.
    /// </summary>
    public SyncReceiptsLocal RecordArrival(Guid from, string method, DateTimeOffset writtenAt)
    {
        if (!SyncReceiptParts.Methods.Contains(method) || from == Guid.Empty)
        {
            return this;
        }

        return Change(from, row =>
        {
            DateTimeOffset previous = row.ByMethod is not null && row.ByMethod.TryGetValue(method, out DateTimeOffset held) ? held : DateTimeOffset.MinValue;
            return row with
            {
                Held = writtenAt > row.Held ? writtenAt : row.Held,
                ByMethod = With(row.ByMethod, method, writtenAt > previous ? writtenAt : previous),
                Failed = Without(row.Failed, method),
            };
        });
    }

    /// <summary>The last attempt to read <paramref name="from"/>'s file by <paramref name="method"/> failed for <paramref name="reason"/> (a code of <see cref="SyncReceiptParts.Failures"/>).</summary>
    public SyncReceiptsLocal RecordFailure(Guid from, string method, string reason)
    {
        if (!SyncReceiptParts.Methods.Contains(method) || !SyncReceiptParts.Failures.Contains(reason) || from == Guid.Empty)
        {
            return this;
        }

        return Change(from, row => row with { Failed = With(row.Failed, method, reason) });
    }

    /// <summary>The outcome of the last accept of <paramref name="from"/>'s file written at <paramref name="at"/>, per part code. Unknown codes and states are ignored.</summary>
    public SyncReceiptsLocal RecordParts(Guid from, DateTimeOffset at, IReadOnlyDictionary<string, string> states)
    {
        if (from == Guid.Empty)
        {
            return this;
        }

        return Change(from, row =>
        {
            IReadOnlyDictionary<string, SyncPartReceipt>? parts = row.Parts;
            foreach ((string code, string state) in states)
            {
                if (SyncReceiptParts.Codes.Contains(code) && SyncReceiptParts.States.Contains(state))
                {
                    parts = With(parts, code, new SyncPartReceipt(at, state));
                }
            }

            return row with { Parts = parts };
        });
    }

    /// <summary>A goal history month file of <paramref name="from"/> was merged ("yyyy-MM", the version's written time); the newest <see cref="SyncReceiptParts.MaxMonths"/> months stay.</summary>
    public SyncReceiptsLocal RecordHistory(Guid from, string month, DateTimeOffset version)
    {
        if (from == Guid.Empty || !SyncReceiptParts.IsMonth(month))
        {
            return this;
        }

        return Change(from, row =>
        {
            List<SyncHistoryReceipt> months = (row.History ?? []).Where(item => item.Month != month).ToList();
            months.Add(new SyncHistoryReceipt(month, version));
            return row with { History = months.OrderByDescending(item => item.Month, StringComparer.Ordinal).Take(SyncReceiptParts.MaxMonths).ToList() };
        });
    }

    /// <summary>Forgets the rows of devices that are gone (removed, ADR 54).</summary>
    public SyncReceiptsLocal Forget(IReadOnlySet<Guid> ids)
    {
        return ids.Count == 0 ? this : new SyncReceiptsLocal(Senders.Where(row => !ids.Contains(row.From)).ToList());
    }

    /// <summary>
    /// The list this device writes into its file: a row for each of <paramref name="active"/> except <paramref name="self"/>, never a removed
    /// device, cleaned and bounded by <see cref="SyncReceiptParts.Clean"/> (the first <see cref="SyncReceiptParts.MaxEntries"/> rows); a row
    /// that holds nothing of the sender yet (only a failure) is written too, with the earliest time. Null when there is no other device
    /// (works alone: nothing is written).
    /// </summary>
    public IReadOnlyList<SyncReceipt>? ForFile(Guid self, IReadOnlySet<Guid> active)
    {
        List<SyncReceipt> rows = Senders.Where(row => row.From != self && active.Contains(row.From)).ToList();
        return rows.Count == 0 ? null : SyncReceiptParts.Clean(rows);
    }

    private SyncReceiptsLocal Change(Guid from, Func<SyncReceipt, SyncReceipt> change)
    {
        List<SyncReceipt> senders = Senders.ToList();
        int index = senders.FindIndex(row => row.From == from);
        if (index < 0)
        {
            senders.Add(change(new SyncReceipt(from, DateTimeOffset.MinValue)));
        }
        else
        {
            senders[index] = change(senders[index]);
        }

        return new SyncReceiptsLocal(senders);
    }

    private static Dictionary<string, T> With<T>(IReadOnlyDictionary<string, T>? items, string key, T value)
    {
        var copy = new Dictionary<string, T>();
        bool placed = false;
        foreach ((string name, T existing) in items ?? new Dictionary<string, T>())
        {
            bool same = name == key;
            copy[name] = same ? value : existing;
            placed |= same;
        }

        if (!placed)
        {
            copy[key] = value;
        }

        return copy;
    }

    private static IReadOnlyDictionary<string, T>? Without<T>(IReadOnlyDictionary<string, T>? items, string key)
    {
        if (items is null)
        {
            return null;
        }

        var copy = items.Where(pair => pair.Key != key).ToDictionary(pair => pair.Key, pair => pair.Value);
        return copy.Count == 0 ? null : copy;
    }
}
