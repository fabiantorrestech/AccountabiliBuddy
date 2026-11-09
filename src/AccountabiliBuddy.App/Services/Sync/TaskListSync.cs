using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// ADR 72's part of the sync file on this PC: it adds the task history, the settle records, the Auto-saved list, the handovers
/// and the handover receipts to this PC's own file, and takes them in from the other devices' files: other PCs' history, the
/// merged records and list, items handed to this PC to keep (and the receipts that end a handover). It also hands local items
/// over to the device the user chose. Only while this PC shares its task lists (ADR 71). Use from the UI thread only.
/// </summary>
public sealed class TaskListSync
{
    private readonly SessionTasksService _tasks;
    private readonly PastSettledStore _settled;
    private readonly AutoSavedTaskStore _autoSaved;
    private readonly ReceivedTaskHistoryStore _received;
    private readonly HandoverOutbox _outbox;
    private readonly KeptPastStore _kept;
    private readonly LaterStore _later;
    private readonly ComebackStore _comebacks;
    private readonly Func<Guid?> _self;
    private readonly Func<string> _selfName;
    private readonly TimeProvider _time;

    public TaskListSync(
        SessionTasksService tasks, PastSettledStore settled, AutoSavedTaskStore autoSaved, ReceivedTaskHistoryStore received,
        HandoverOutbox outbox, KeptPastStore kept, LaterStore later, ComebackStore comebacks, Func<Guid?> self, Func<string> selfName,
        TimeProvider time)
    {
        _tasks = tasks;
        _settled = settled;
        _autoSaved = autoSaved;
        _received = received;
        _outbox = outbox;
        _kept = kept;
        _later = later;
        _comebacks = comebacks;
        _self = self;
        _selfName = selfName;
        _time = time;
        _settled.Arrived += (_, records) => _ = _tasks.ApplySettledAsync(records);
        _outbox.Expired += (_, handover) => _ = TakeBackAsync(handover);
    }

    /// <summary>A handover nobody took in 30 days came back here (the page tells the user once). Raised with the item's title.</summary>
    public event EventHandler<string>? CameBack;

    /// <summary>This PC's file with ADR 72's parts added (and its lists' local items as stubs).</summary>
    public SyncFile Decorate(SyncFile file)
    {
        IReadOnlyList<SyncHandover> handovers = _outbox.ForSync();
        IReadOnlyList<SyncHandoverAck> acks = _outbox.AcksForSync();
        return file with
        {
            TaskHistory = _tasks.TaskHistoryForSync(),
            PastSettled = _settled.ForSync(),
            AutoSaved = _autoSaved.ForSync(),
            Handovers = handovers.Count > 0 ? handovers : null,
            HandoverAcks = acks.Count > 0 ? acks : null,
        };
    }

    /// <summary>Takes ADR 72's parts in from another device's file.</summary>
    public async Task ReceiveAsync(SyncFile file)
    {
        Guid? self = _self();
        if (self is not Guid me || file.Device.DeviceId == me)
        {
            return;
        }

        if (file.TaskHistory is { } history && file.Device.Kind == SyncDeviceCard.KindPc)
        {
            await _received.ReceiveAsync(file.Device.DeviceId, file.Device.Name, history);
        }

        if (file.PastSettled is { } settled)
        {
            await _settled.MergeAsync(settled.OfType<SyncPastSettled>().ToList());
        }

        if (file.AutoSaved is { } autoSaved)
        {
            await _autoSaved.MergeAsync(autoSaved.OfType<SyncAutoSavedItem>().Select(item => item with { Title = item.Title ?? string.Empty }).ToList());
        }

        await _outbox.AcknowledgedAsync(file.Device.DeviceId, file.HandoverAcks);
        foreach (SyncHandover handover in _outbox.NewFor(me, file.Handovers))
        {
            await AdoptAsync(handover, me);
            await _outbox.TakenAsync(handover);
        }
    }

    /// <summary>
    /// Keeps a task or item on <paramref name="keeper"/> as a local item (ADR 72), or syncs it again with null. Kept on another device,
    /// it leaves this one at once and travels sealed to that device until its receipt shows.
    /// </summary>
    /// <returns>False when the item can't be made local (not found, or in the running checklist).</returns>
    public async Task<bool> SetLocalAsync(LocalItemKind kind, Guid id, Guid? keeper)
    {
        Guid? self = _self();
        bool elsewhere = keeper is Guid other && other != self;
        DateTimeOffset now = _time.GetUtcNow();
        switch (kind)
        {
            case LocalItemKind.NextTask:
                SyncNextTask? next = await _tasks.SetNextTaskLocalAsync(id, keeper);
                if (next is null)
                {
                    return false;
                }

                if (elsewhere)
                {
                    await _outbox.SendAsync(new SyncHandover(keeper!.Value, id, SyncHandover.NextTasks, next.EditedAt, now, NextTask: next with { LocalTo = keeper }));
                }

                return true;
            case LocalItemKind.Later:
                SyncLaterItem? later = await _later.SetLocalAsync(id, keeper);
                if (later is null)
                {
                    return false;
                }

                if (elsewhere)
                {
                    await _outbox.SendAsync(new SyncHandover(keeper!.Value, id, SyncHandover.Later, later.EditedAt, now, LaterItem: later));
                }

                return true;
            case LocalItemKind.ComeBack:
                SyncParked? parked = await _comebacks.SetLocalAsync(id, keeper, self);
                if (parked is null)
                {
                    return false;
                }

                if (elsewhere)
                {
                    await _outbox.SendAsync(new SyncHandover(keeper!.Value, id, SyncHandover.Parked, parked.EditedAt, now, ParkedItem: parked));
                }

                return true;
            case LocalItemKind.AutoSaved:
                SyncAutoSavedItem? saved = await _autoSaved.SetLocalAsync(id, keeper);
                if (saved is null)
                {
                    return false;
                }

                if (elsewhere)
                {
                    await _outbox.SendAsync(new SyncHandover(keeper!.Value, id, SyncHandover.AutoSaved, saved.EditedAt, now, AutoSavedItem: saved));
                }

                return true;
            case LocalItemKind.PastTask:
                return await SetPastLocalAsync(id, keeper, self, now);
            default:
                return false;
        }
    }

    // A past session's task: this PC's own, another PC's (received), or one kept here.
    private async Task<bool> SetPastLocalAsync(Guid id, Guid? keeper, Guid? self, DateTimeOffset now)
    {
        bool elsewhere = keeper is Guid other && other != self;
        if (_tasks.HistoryTaskOf(id) is SessionTask own && own.Title.Length > 0)
        {
            DateTimeOffset? start = _tasks.HistorySessionStartOf(id);
            await _tasks.SetHistoryLocalAsync(id, keeper);
            if (elsewhere && self is Guid me)
            {
                var past = new SyncKeptPast(me, _selfName(), start, own with { LocalTo = keeper }, now);
                await _outbox.SendAsync(new SyncHandover(keeper!.Value, id, SyncHandover.History, now, now, PastTask: past));
            }

            return true;
        }

        if (_kept.Find(id) is SyncKeptPast kept)
        {
            // Kept here: synced again goes back to its PC; kept on yet another device goes there.
            await _kept.RemoveAsync(id);
            if (keeper is null)
            {
                await _settled.SetAsync(id, SyncPastSettled.Open);
                await _outbox.SendAsync(new SyncHandover(kept.SourceDeviceId, id, SyncHandover.History, now, now, PastTask: kept with { Task = kept.Task with { LocalTo = null }, EditedAt = now }));
            }
            else if (elsewhere)
            {
                await _settled.SetAsync(id, SyncPastSettled.Local, keeper);
                await _outbox.SendAsync(new SyncHandover(keeper!.Value, id, SyncHandover.History, now, now, PastTask: kept with { Task = kept.Task with { LocalTo = keeper }, EditedAt = now }));
            }
            else
            {
                await _kept.KeepAsync(kept);
            }

            return true;
        }

        if (_received.Find(id) is ReceivedPastTask received && keeper is Guid target)
        {
            // Another PC's task: its PC stubs it on the settle record; it travels to the keeper (this PC keeps it at once).
            await _settled.SetAsync(id, SyncPastSettled.Local, target);
            var past = new SyncKeptPast(received.DeviceId, received.DeviceName, received.SessionStart, received.Task with { LocalTo = target }, now);
            if (target == self)
            {
                await _kept.KeepAsync(past);
            }
            else
            {
                await _outbox.SendAsync(new SyncHandover(target, id, SyncHandover.History, now, now, PastTask: past));
            }

            return true;
        }

        return false;
    }

    // An item handed to this PC to keep (or handed back to its PC).
    private async Task AdoptAsync(SyncHandover handover, Guid me)
    {
        switch (handover.List)
        {
            case SyncHandover.NextTasks when handover.NextTask is { } next:
                await _tasks.AdoptNextTaskAsync(next with { LocalTo = me });
                break;
            case SyncHandover.Later when handover.LaterItem is { } later:
                await _later.AdoptAsync(later with { LocalTo = me });
                break;
            case SyncHandover.Parked when handover.ParkedItem is { } parked:
                await _comebacks.AdoptAsync(parked with { LocalTo = me });
                break;
            case SyncHandover.AutoSaved when handover.AutoSavedItem is { } saved:
                await _autoSaved.AdoptAsync(saved with { LocalTo = me });
                break;
            case SyncHandover.History when handover.PastTask is { } past:
                if (past.SourceDeviceId == me)
                {
                    // Back home: the task is synced again in this PC's history, or stays local here.
                    await _tasks.RestoreHistoryTaskAsync(past.Task with { LocalTo = null });
                    if (past.Task.LocalTo == me)
                    {
                        await _tasks.SetHistoryLocalAsync(past.Task.Id, me);
                    }
                }
                else
                {
                    await _kept.KeepAsync(past with { Task = past.Task with { LocalTo = me } });
                }

                break;
        }
    }

    // A handover nobody took: the item comes back here, local to this PC.
    private async Task TakeBackAsync(SyncHandover handover)
    {
        Guid? self = _self();
        if (self is not Guid me)
        {
            return;
        }

        await AdoptAsync(handover with { To = me }, me);
        string title = handover.NextTask?.Title ?? handover.LaterItem?.Title ?? handover.ParkedItem?.Text ?? handover.AutoSavedItem?.Title ?? handover.PastTask?.Task.Title ?? string.Empty;
        CameBack?.Invoke(this, title);
    }
}
