using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// Removing devices from the group and learning of a removal (ADR 54). Removing makes a new link code, writes the rekey
/// file (<see cref="RekeyCodec"/>) to every sync method in use, and switches this PC to the new key in place
/// (<see cref="SyncIdentityService.SwitchKeyAsync"/>), keeping every device it has received. A rekey file another device
/// wrote comes in through <see cref="AcceptRekeyAsync"/> (every method hands it to the sync coordinator, which passes it
/// here): the PC unwraps its copy of the new code and does the same switch. A rekey that names this PC (or leaves it out)
/// puts it in the <see cref="Unlinked"/> state instead: the group's key is dropped, so nothing syncs by any method, until the
/// user picks <see cref="ContinueUnsyncedAsync"/> or this PC joins a group again. The removed devices are kept in the
/// local document "sync.removed"; their files are refused (<see cref="SyncCoordinator"/>) until one opens with the new
/// key, which means the device was linked again. A rekey is tied to the group it was made for (its old group id), so one
/// that was already applied, or that comes after the PC moved on, is ignored. Use from the UI thread only.
/// Rekey files are one per maker (<see cref="RekeyCodec.FileNameFor"/>): when two devices remove at the same time every device
/// applies the one whose maker has the lower id (<see cref="RekeyCodec.CompareMakers"/>), and a maker looks for another's file
/// before and after writing its own.
/// </summary>
public sealed class SyncRemovalService
{
    private const string DocumentKey = "sync.removed";

    /// <summary>Why a removal can't happen while a focus session the user started is running.</summary>
    public const string LockedReason = "Devices can't be removed during a focus session.";

    private readonly SqliteJsonDocumentStore _store;
    private readonly SyncIdentityService _identity;
    private readonly ReceivedDevicesService _received;
    private readonly SessionController _sessions;

    // Changes and their saves run one at a time, so an older list never overwrites a newer one.
    private readonly SemaphoreSlim _changing = new(1, 1);

    // Rekeys for the current group that came in and wait for the others of the same read pass, one per maker (see AcceptRekeyAsync).
    private static readonly TimeSpan PickWindow = TimeSpan.FromSeconds(3);

    // When an apply could not be kept (storage busy), the rekeys wait this long and are tried again.
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    private readonly List<PendingRekey> _pending = [];
    private bool _applyScheduled;

    private List<RemovedDevice> _removed = [];
    private UnlinkedState? _unlinked;

    public SyncRemovalService(SqliteJsonDocumentStore store, SyncIdentityService identity, ReceivedDevicesService received, SessionController sessions)
    {
        _store = store;
        _identity = identity;
        _received = received;
        _sessions = sessions;
        // Joining or making a group ends "unlinked".
        identity.Changed += (_, _) => OnIdentityChanged();
    }

    /// <summary>The sync methods that carry the rekey file (folder, Drive, relay); set by the app.</summary>
    public IReadOnlyList<IRekeyPublisher> Publishers { get; set; } = [];

    /// <summary>The devices removed from the group, in the order they were recorded.</summary>
    public IReadOnlyList<RemovedDevice> Removed => _removed;

    /// <summary>Set while this PC is unlinked (removed from its group, or cut off by a removal); null otherwise. The UI asks the user to choose.</summary>
    public UnlinkedState? Unlinked => _unlinked;

    /// <summary>Raised when the removed list or <see cref="Unlinked"/> changes.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// False while a focus session the user started runs (no escape hatches: the same lock as "Leave group") and while this
    /// PC isn't in a group. <see cref="BlockedReason"/> says which.
    /// </summary>
    public bool CanRemoveNow => BlockedReason is null;

    /// <summary>Why <see cref="RemoveAsync"/> would refuse right now; null when it wouldn't.</summary>
    public string? BlockedReason =>
        _sessions.IsEnforcing ? LockedReason
        : _identity.Key is null ? "This PC isn't linked to a device group."
        : null;

    public void LoadBlocking()
    {
        try
        {
            StoredRemoved? stored = Task.Run(() => _store.LoadAsync<StoredRemoved>(DocumentKey)).GetAwaiter().GetResult();
            _removed = stored?.Devices?.Where(device => device is { Name: not null } && device.DeviceId != Guid.Empty).ToList() ?? [];
            // A PC that is in a group again (the document was left over) is not unlinked.
            _unlinked = _identity.Key is null && stored?.Unlinked is { RemovedByName: not null } unlinked ? unlinked : null;
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            _removed = [];
            _unlinked = null;
        }
    }

    public bool IsRemoved(Guid deviceId) => _removed.Any(device => device.DeviceId == deviceId);

    /// <returns>The record of a removed device; null when it isn't one.</returns>
    public RemovedDevice? RemovedAs(Guid deviceId) => _removed.FirstOrDefault(device => device.DeviceId == deviceId);

    /// <summary>
    /// The devices that would be cut off along with <paramref name="deviceIds"/> because they can't receive the new key (ADR 54): an
    /// app from before ADR 54 (no public key in its card), and devices this PC only knows from the others' "seen" lists. The remove
    /// dialog names them before the user confirms. (A key that isn't a valid P-256 point shows up only in <see cref="RemovalResult.NotWrapped"/>.)
    /// </summary>
    public IReadOnlyList<NotWrappedDevice> NotWrappedFor(IReadOnlyList<Guid> deviceIds)
    {
        IReadOnlyList<SyncDeviceCard> cards = _received.Devices.Select(device => device.File.Device).ToList();
        var known = cards.Select(card => card.DeviceId).ToHashSet();
        List<NotWrappedDevice> result = cards
            .Where(card => !deviceIds.Contains(card.DeviceId) && !IsRemoved(card.DeviceId) && string.IsNullOrEmpty(card.PublicKey))
            .Select(card => new NotWrappedDevice(card.DeviceId, card.Name))
            .ToList();
        result.AddRange(cards.SelectMany(card => card.Seen.Keys)
            .Where(id => id != _identity.DeviceId && !known.Contains(id) && !deviceIds.Contains(id) && !IsRemoved(id))
            .Distinct()
            .Select(id => new NotWrappedDevice(id, "A device this PC hasn't received a file from")));
        return result;
    }

    /// <summary>
    /// Restoring a backup (ADR 55) brought a device's data in as a received device: the device is recorded as removed, so
    /// it shows as "[name] (removed)" and its files aren't taken in (a file from it that opens with this group's key and was written
    /// after <paramref name="writtenAt"/> clears the mark, as for any removed device). Nothing changes when it is recorded already.
    /// </summary>
    public async Task RecordRestoredAsync(Guid deviceId, string name, DateTimeOffset writtenAt)
    {
        await _changing.WaitAsync();
        try
        {
            if (!IsRemoved(deviceId))
            {
                _removed = _removed.Append(new RemovedDevice(deviceId, name, writtenAt, RemovedDevice.KeepData)).ToList();
                await SaveAsync();
            }
        }
        finally
        {
            _changing.Release();
        }
    }

    /// <summary>
    /// Removes <paramref name="deviceIds"/> from the group: writes this PC's rekey file to the sync methods, then switches this
    /// PC to the new key. Their received data is left as it is; <see cref="ApplyDataChoiceAsync"/> is the caller's next step
    /// (ADR 55). Nothing changes when it can't be done, or when no method took the file (the other devices would never hear of it).
    /// Two devices removing at the same time would split the group, so: a rekey another device already wrote for this group is
    /// applied here instead and the call returns a <see cref="RemovalResult.Conflict"/> (look and try again), and after writing,
    /// a rekey from a device with a lower id (the one every device picks, <see cref="RekeyCodec.CompareMakers"/>) is applied
    /// instead of this one. The network calls run outside <c>_changing</c>: the Drive and relay read passes hold their own lock
    /// while they hand a rekey in, so taking <c>_changing</c> first would deadlock with them.
    /// </summary>
    public async Task<RemovalResult> RemoveAsync(IReadOnlyList<Guid> deviceIds)
    {
        if (BlockedReason is string blocked)
        {
            return RemovalResult.Failed(blocked);
        }

        if (deviceIds.Count == 0 || deviceIds.Contains(_identity.DeviceId))
        {
            return RemovalResult.Failed("Pick other devices to remove; this PC leaves the group with Leave group.");
        }

        SyncKey oldKey = _identity.Key!;
        IReadOnlyList<SyncDeviceCard> cards = _received.Devices.Select(device => device.File.Device).ToList();
        List<RekeyRemovedDevice> removing = deviceIds
            .Distinct()
            .Select(id => new RekeyRemovedDevice(id, cards.FirstOrDefault(card => card.DeviceId == id)?.Name ?? "Unknown device"))
            .ToList();
        List<SyncDeviceCard> staying = cards.Where(card => !deviceIds.Contains(card.DeviceId) && !IsRemoved(card.DeviceId)).ToList();

        // This PC is among the recipients, so reading its own file (after a crash between writing and switching) gives it the new
        // key instead of "unlinked".
        List<RekeyRecipient> recipients = staying
            .Where(card => !string.IsNullOrEmpty(card.PublicKey))
            .Select(card => new RekeyRecipient(card.DeviceId, card.PublicKey!))
            .Append(new RekeyRecipient(_identity.DeviceId, _identity.DeviceKey.PublicKeyBase64))
            .ToList();

        RekeyBuild build = RekeyBuilder.Build(oldKey, new RekeyRemovedDevice(_identity.DeviceId, _identity.DeviceName), removing, recipients, _sessions.Now);

        // The devices that stay but can't get the new key: an app from before ADR 54 (no public key in its card), a key that isn't
        // a P-256 point, or a device this PC never received a file from (only named in the others' "seen" lists). They are cut
        // off like the removed ones; the remove dialog names them.
        var known = cards.Select(card => card.DeviceId).ToHashSet();
        List<Guid> notWrapped = staying.Where(card => string.IsNullOrEmpty(card.PublicKey)).Select(card => card.DeviceId)
            .Concat(build.NotWrapped)
            .Concat(cards.SelectMany(card => card.Seen.Keys)
                .Where(id => id != _identity.DeviceId && !known.Contains(id) && !deviceIds.Contains(id) && !IsRemoved(id)))
            .Distinct()
            .ToList();

        // Where another device's rekey for this group would be, if there is one.
        List<string> otherNames = known.Concat(cards.SelectMany(card => card.Seen.Keys))
            .Where(id => id != _identity.DeviceId)
            .Distinct()
            .Select(id => RekeyCodec.FileNameFor(oldKey.GroupId, id))
            .ToList();

        if (await ApplyOthersRekeyAsync(oldKey, otherNames, lowerThanThisPcOnly: false))
        {
            return RemovalResult.ConflictWith("Another device just changed the device group. Its change was applied here; check your devices and try again.");
        }

        string fileName = RekeyCodec.FileNameFor(oldKey.GroupId, _identity.DeviceId);
        int written = 0;
        int failed = 0;
        foreach (IRekeyPublisher publisher in Publishers)
        {
            bool? result = await publisher.PublishRekeyAsync(build.SealedFile, fileName);
            written += result == true ? 1 : 0;
            failed += result == false ? 1 : 0;
        }

        if (written == 0)
        {
            return RemovalResult.Failed(failed == 0
                ? "Turn on a sync method (a sync folder, Google Drive or the relay) first: the other devices learn of the change through it."
                : "The change couldn't be written to any sync method. Check the sync status and try again.");
        }

        // Written first, then looked for: of two devices that wrote at the same time, the one with the higher id sees the other's file.
        if (await ApplyOthersRekeyAsync(oldKey, otherNames, lowerThanThisPcOnly: true))
        {
            return RemovalResult.ConflictWith("Another device changed the device group at the same time. Its change was applied here instead; check your devices and try again.");
        }

        await _changing.WaitAsync();
        try
        {
            // This PC's own file, read meanwhile, already switched it to the new key (it is among the recipients): done.
            if (SyncKeyCode.TryParse(build.NewCode, out SyncKey? newKey) && newKey.SameGroupAs(_identity.Key))
            {
                return new RemovalResult(true, null, notWrapped, failed);
            }

            // Another rekey switched or unlinked this PC while the file was being written.
            if (_identity.Key is not SyncKey current || !current.SameGroupAs(oldKey))
            {
                return RemovalResult.ConflictWith("The device group changed while this was being saved. Check your devices and try again.");
            }

            await RecordRemovedAsync(removing.Select(device => (device.DeviceId, device.Name)), DateTimeOffset.FromUnixTimeSeconds(build.Payload.Epoch));
            await _identity.SwitchKeyAsync(build.NewCode, removing.Select(device => device.DeviceId).ToList());
            return new RemovalResult(true, null, notWrapped, failed);
        }
        finally
        {
            _changing.Release();
        }
    }

    /// <summary>
    /// Takes in a rekey file (ADR 54) another device wrote, as <see cref="SyncCoordinator"/> hands it over. A rekey for a
    /// group this PC has already left or switched away from is ignored, which also makes a repeated file harmless. A rekey for
    /// this PC's group is kept for a few seconds before it is applied: if two devices removed at the same time, the one with the
    /// lower id wins (<see cref="RekeyCodec.CompareMakers"/>), whichever file arrived first. When the chosen rekey names this PC
    /// (or leaves it out) this PC becomes <see cref="Unlinked"/>.
    /// </summary>
    /// <param name="sealedBytes">The file, starting "ABR1".</param>
    /// <param name="currentKey">The key this PC holds now.</param>
    /// <returns>Null when taken in or ignored; <see cref="SyncCoordinator.CannotReadReason"/> when it is this group's but doesn't open.</returns>
    public Task<string?> AcceptRekeyAsync(byte[] sealedBytes, SyncKey currentKey)
    {
        if (RekeyCodec.GroupIdOf(sealedBytes) != currentKey.GroupId)
        {
            return Task.FromResult<string?>(null);
        }

        if (RekeyCodec.Open(sealedBytes, currentKey) is not RekeyPayload payload)
        {
            return Task.FromResult<string?>(SyncCoordinator.CannotReadReason);
        }

        // One per maker: a newer file from the same maker replaces its older one.
        _pending.RemoveAll(pending => pending.Payload.RemovedBy.DeviceId == payload.RemovedBy.DeviceId && pending.Payload.Epoch <= payload.Epoch);
        if (_pending.All(pending => pending.Payload.RemovedBy.DeviceId != payload.RemovedBy.DeviceId))
        {
            _pending.Add(new PendingRekey(payload, currentKey));
        }

        if (!_applyScheduled)
        {
            _applyScheduled = true;
            _ = ApplyPendingAfterWindowAsync(PickWindow);
        }

        return Task.FromResult<string?>(null);
    }

    // Waits for the other rekey files of the same read pass, then applies the one every device picks. If it can't be kept
    // right now the rekeys are put back and tried again later: the readers have already marked their files as seen, so
    // nothing else would bring them back.
    private async Task ApplyPendingAfterWindowAsync(TimeSpan wait)
    {
        await Task.Delay(wait);
        _applyScheduled = false;
        // Only rekeys for the group this PC is in now: one for a group it already left (it was buffered while another apply ran)
        // must not win the pick and take the valid one down with it.
        List<PendingRekey> pending = _pending.Where(candidate => _identity.Key?.SameGroupAs(candidate.Key) == true).ToList();
        _pending.Clear();
        PendingRekey? chosen = null;
        foreach (PendingRekey candidate in pending)
        {
            if (chosen is null || RekeyCodec.CompareMakers(candidate.Payload.RemovedBy.DeviceId, chosen.Payload.RemovedBy.DeviceId) < 0)
            {
                chosen = candidate;
            }
        }

        if (chosen is not null)
        {
            try
            {
                await ApplyAsync(chosen.Payload, chosen.Key);
            }
            catch (SqliteException)
            {
                _pending.AddRange(pending.Where(candidate => _pending.All(waiting => waiting.Payload.RemovedBy.DeviceId != candidate.Payload.RemovedBy.DeviceId)));
                if (!_applyScheduled)
                {
                    _applyScheduled = true;
                    _ = ApplyPendingAfterWindowAsync(RetryDelay);
                }
            }
        }
    }

    // Applies the best rekey among files other devices wrote for oldKey's group (the lowest maker id, and only those below this
    // PC's when asked). False when there is none.
    private async Task<bool> ApplyOthersRekeyAsync(SyncKey oldKey, IReadOnlyCollection<string> fileNames, bool lowerThanThisPcOnly)
    {
        RekeyPayload? best = null;
        foreach (IRekeyPublisher publisher in Publishers)
        {
            foreach (byte[] bytes in await publisher.FindRekeysAsync(fileNames))
            {
                if (RekeyCodec.GroupIdOf(bytes) != oldKey.GroupId || RekeyCodec.Open(bytes, oldKey) is not RekeyPayload payload)
                {
                    continue;
                }

                Guid maker = payload.RemovedBy.DeviceId;
                if (maker == _identity.DeviceId || (lowerThanThisPcOnly && RekeyCodec.CompareMakers(maker, _identity.DeviceId) > 0))
                {
                    continue;
                }

                if (best is null || RekeyCodec.CompareMakers(maker, best.RemovedBy.DeviceId) < 0)
                {
                    best = payload;
                }
            }
        }

        if (best is null)
        {
            return false;
        }

        await ApplyAsync(best, oldKey);
        return true;
    }

    // Applies one rekey: switch to the new key, or become unlinked when it names this PC or leaves it out.
    private async Task ApplyAsync(RekeyPayload payload, SyncKey rekeyKey)
    {
        RekeyReading reading = RekeyReader.Read(payload, _identity.DeviceId, _identity.DeviceKey, rekeyKey.GroupId);
        await _changing.WaitAsync();
        try
        {
            // Another file already switched (or unlinked) this PC while this one waited.
            if (_identity.Key is not SyncKey key || !key.SameGroupAs(rekeyKey))
            {
                return;
            }

            // When the removal happened, by its maker's clock, not when this PC learned of it: files a device wrote while this PC was away are judged against it.
            var others = payload.Removed.Where(device => device.DeviceId != _identity.DeviceId).Select(device => (device.DeviceId, device.Name)).ToList();
            await RecordRemovedAsync(others, DateTimeOffset.FromUnixTimeSeconds(payload.Epoch));
            if (reading is { Kind: RekeyReadingKind.NewKey, NewCode: string newCode })
            {
                await _identity.SwitchKeyAsync(newCode, payload.Removed.Select(device => device.DeviceId).ToList());
            }
            else
            {
                // Removed, or left out of the rekey: there is no new key to switch to. The key is dropped (nothing syncs by any
                // method), what was received stays, and the user chooses what happens next.
                _unlinked = new UnlinkedState(_sessions.Now, reading.Kind == RekeyReadingKind.Removed, payload.RemovedBy.Name);
                await SaveAsync();
                await _identity.DropKeyKeepingDataAsync();
            }
        }
        finally
        {
            _changing.Release();
        }
    }

    /// <summary>
    /// "Continue unsynced here" (ADR 54): this PC stays out of the group and keeps its own data, and what it had received from
    /// the group's devices stays as it was at the last sync (shown as not syncing); a later join forgets it, as any join does.
    /// The removed list stays too, so removed devices keep their label. Refused during a focus session the user started.
    /// </summary>
    /// <returns>Null when done; otherwise why not.</returns>
    public async Task<string?> ContinueUnsyncedAsync()
    {
        if (_sessions.IsEnforcing)
        {
            return LockedReason;
        }

        await _changing.WaitAsync();
        try
        {
            _unlinked = null;
            await SaveAsync();
            await _identity.LeaveAsync();
            return null;
        }
        finally
        {
            _changing.Release();
        }
    }

    /// <summary>
    /// "Add this PC again" (ADR 54): joins the group of a new link code from a device that is still linked, keeping this PC's
    /// device id. That ends <see cref="Unlinked"/>; the other devices accept this PC's files again once one opens with the new key.
    /// Like any join it forgets what was received from the earlier group.
    /// </summary>
    /// <returns>False when the code isn't a link code.</returns>
    public Task<bool> RejoinAsync(string code) => _identity.JoinAsync(code);

    /// <summary>
    /// Applies the user's choice for a removed device's received data (ADR 55). <see cref="RemovedDevice.EraseData"/> forgets
    /// what this PC received from it (its sessions, drift and the rest of its file); <see cref="RemovedDevice.KeepData"/>
    /// keeps it, shown as removed. The choice is recorded either way. Refused during a focus session the user started (erasing
    /// a phone's copy mid-session would drop its pickup evidence).
    /// </summary>
    /// <returns>Null when done; otherwise why not.</returns>
    public async Task<string?> ApplyDataChoiceAsync(Guid deviceId, string choice)
    {
        if (_sessions.IsEnforcing)
        {
            return LockedReason;
        }

        if (choice == RemovedDevice.EraseData)
        {
            await _received.ForgetAsync(deviceId);
        }

        await _changing.WaitAsync();
        try
        {
            if (RemovedAs(deviceId) is RemovedDevice device)
            {
                _removed = _removed.Select(existing => existing == device ? device with { DataChoice = choice } : existing).ToList();
                await SaveAsync();
            }

            return null;
        }
        finally
        {
            _changing.Release();
        }
    }

    /// <summary>A removed device wrote a file that opens with the new key, so it was linked again: its files are accepted again.</summary>
    public async Task ClearRemovedAsync(Guid deviceId)
    {
        await _changing.WaitAsync();
        try
        {
            if (IsRemoved(deviceId))
            {
                _removed = _removed.Where(device => device.DeviceId != deviceId).ToList();
                await SaveAsync();
            }
        }
        finally
        {
            _changing.Release();
        }
    }

    // Adds the devices not already recorded (a repeated rekey, or a second file saying the same), then saves.
    private async Task RecordRemovedAsync(IEnumerable<(Guid DeviceId, string Name)> devices, DateTimeOffset removedAt)
    {
        foreach ((Guid deviceId, string name) in devices.Where(device => !IsRemoved(device.DeviceId)))
        {
            _removed = _removed.Append(new RemovedDevice(deviceId, name, removedAt)).ToList();
        }

        await SaveAsync();
    }

    // This PC joined or made a group: it is no longer unlinked.
    private void OnIdentityChanged()
    {
        if (_unlinked is not null && _identity.Key is not null)
        {
            _unlinked = null;
            _ = SaveWhenFreeAsync();
        }
    }

    private async Task SaveWhenFreeAsync()
    {
        await _changing.WaitAsync();
        try
        {
            await SaveAsync();
        }
        finally
        {
            _changing.Release();
        }
    }

    private Task SaveAsync()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        return _store.SaveAsync(DocumentKey, new StoredRemoved(_removed, _unlinked));
    }

    private sealed record PendingRekey(RekeyPayload Payload, SyncKey Key);

    /// <param name="Devices">The devices removed from the group.</param>
    /// <param name="Unlinked">Set while this PC is unlinked.</param>
    private sealed record StoredRemoved(List<RemovedDevice>? Devices, UnlinkedState? Unlinked);
}
