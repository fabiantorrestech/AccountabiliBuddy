using System.Net;
using AccountabiliBuddy.App.Services.Goals;
using AccountabiliBuddy.Core.Goals;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// The shared goal history on this PC (ADR 79 "shared history", owner 2026-11-14; GS3b): each device's month files and the yearly
/// roll-up travel as sealed files next to the sync files, on Drive (the app's own folder, drive.file) and on the user's own relay, by the
/// rules of <see cref="GoalHistoryRules"/> (the phone's twin gives the same results). One pass per transport, in this order:
/// <list type="number">
/// <item><b>List</b> the transport and pick the files this PC knows by name (<see cref="GoalHistoryNames"/>).</item>
/// <item><b>Read</b> what <see cref="GoalHistoryRules.ToRead"/> says (new or changed since last read; everything while new to the group;
/// a stale device's files in full): merged into the goals AFTER the regular files' goals part (this PC counts itself new to the group
/// until one merged), union by id, newest wins, entry tombstones kept; a file that brought rows of a goal this PC doesn't hold yet is not
/// recorded as read unless the goal's tombstone was pruned here (Q649, the kept set <see cref="GoalHistoryState.PrunedGoals"/>). A year
/// file merged goes into this PC's own <c>goalYearsMerged</c> marker (sent in its regular file).</item>
/// <item><b>Roll up</b> a finished year (from 1 February UTC) when this PC is the lowest-id active PC (adds only; never over a year file it
/// hasn't read; a second PC that thinks it rolls writes its own file, keyed by its id, so nothing is overwritten); then read again so the
/// roller merges and marks its own year file.</item>
/// <item><b>Write</b> this PC's month files: any month whose content changed, at most daily per file, every month on the first run.</item>
/// <item><b>Remove</b> own month files that <see cref="GoalHistoryRules.MayRemoveMonth"/> frees: deleted on Drive; on the relay (which
/// deletes only when a device leaves) simply no longer put again, so they expire.</item>
/// <item><b>Put again</b> own files the relay would expire (25 days, the owner's rule) or that went missing.</item>
/// </list>
/// <b>On the relay</b> (owner 2026-11-13, review fixes): only the PC that rolls the year keeps finished month files there, every other device
/// its open months only, each device at most <see cref="GoalHistoryRules.RelayQuota"/> files, newest months first
/// (<see cref="GoalHistoryRules.RelayKeep"/>); nothing is written while a device of the group is on an app that doesn't know history files
/// (<see cref="GoalHistoryRules.KnowsHistory"/>); a pass makes at most <see cref="IGoalHistoryStore.MaxRequestsPerPass"/> requests; a 429 answer to
/// a put means the group is full and ends the pass quietly (no retry). A month is put again as the same bytes (get, put), like a year file.
/// A file bigger than the store's limit (the relay's 4 MiB) is not written, and the relay keeps room for the devices' sync files. Sealed
/// exactly like the sync files (the group's key, AES-256-GCM); never in a QR file. Logs counts only. Use from the UI thread only.
/// </summary>
public sealed class GoalHistorySync
{
    /// <summary>The relay deletes files untouched for 30 days (ADR 46); own files are put again after this (owner 2026-11-14).</summary>
    public static readonly TimeSpan ReuploadEvery = TimeSpan.FromDays(25);

    /// <summary>A transport is listed and worked through at most this often (a file is still written at most daily).</summary>
    public static readonly TimeSpan RunEvery = TimeSpan.FromMinutes(15);

    /// <summary>A last guard on top of the quota: no new history file is put while the relay's group holds fewer than this many free places.</summary>
    public const int RelayHeadroom = 4;

    private static readonly TimeSpan RollEvery = TimeSpan.FromDays(1);
    private static readonly TimeSpan RetryUnreadableAfter = TimeSpan.FromDays(1);

    private readonly GoalsService _goals;
    private readonly Func<GoalSyncLists?> _held;
    private readonly GoalHistoryStateStore _stateStore;
    private readonly TimeProvider _time;
    private readonly Func<Guid?> _self;
    private readonly Func<SyncKey?> _key;
    private readonly Func<IReadOnlyList<GoalHistoryDeviceInfo>> _devices;
    private readonly Func<bool> _enabled;
    private readonly Action<string> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, DateTimeOffset> _lastRun = [];
    private readonly Dictionary<string, DateTimeOffset> _unreadableTried = [];
    private GoalSyncLists? _previousGoals;
    private HashSet<string>? _names;
    private string _namesKey = string.Empty;

    /// <param name="held">This PC's lists as the sync file carries them, Deep work composed (<see cref="GoalListSync.Held"/>); null while the goals aren't ready.</param>
    /// <param name="self">This PC's device id; null or empty while it isn't in a group.</param>
    /// <param name="key">The group's key.</param>
    /// <param name="devices">The devices this PC has received files from.</param>
    /// <param name="enabled">The goals travel (this PC shares its task lists, ADR 71).</param>
    /// <param name="log">Counts only.</param>
    public GoalHistorySync(
        GoalsService goals, Func<GoalSyncLists?> held, GoalHistoryStateStore stateStore, TimeProvider time, Func<Guid?> self, Func<SyncKey?> key,
        Func<IReadOnlyList<GoalHistoryDeviceInfo>> devices, Func<bool> enabled, Action<string> log)
    {
        _goals = goals;
        _held = held;
        _stateStore = stateStore;
        _time = time;
        _self = self;
        _key = key;
        _devices = devices;
        _enabled = enabled;
        _log = log;
        _previousGoals = goals.Store.IsReady ? GoalStore.ToSync(goals.Lists) : null;
        goals.Store.Changed += (_, _) => TrackPrunedGoals();
        TrackPrunedGoals();
    }

    /// <summary>Raised when this PC's "merged year files" marker changed: its regular sync file is written again.</summary>
    public event EventHandler? MarkerChanged;

    /// <summary>
    /// A goal history month file of another device was merged (ADR 86 receipts): the device, the month as "yyyy-MM" and the file's listed
    /// time. Ids, a month and a time only; set by the app to record it in the receiver-local record.
    /// </summary>
    public Action<Guid, string, DateTimeOffset>? MonthMerged { get; set; }

    /// <summary>
    /// "Re-read goal history (full)" (SYNC1b): this PC is new to the group on <paramref name="transport"/> again, so the next pass reads every
    /// listed file once, its own too (what a first run does; nothing is deleted, nothing bypasses the pass's request cap, the relay's
    /// quota or the "no download before a goals part merged" gate). The caller then runs a normal pass.
    /// </summary>
    public async Task ForceRereadAsync(string transport)
    {
        if (_key() is not SyncKey key)
        {
            return;
        }

        GoalHistoryTransportState state = StateFor(key).Transport(transport);
        state.NewToGroup = true;
        state.Read.Clear();
        state.ReadTag.Clear();
        _unreadableTried.Clear();
        await _stateStore.SaveAsync();
    }

    /// <summary>
    /// "Send my files again" for the goal history (SYNC1b): this PC's KEPT month files on <paramref name="transport"/> are marked to be put again,
    /// and the caller runs a normal pass. On the relay the "uploaded" time of each own file is forgotten, so a kept month is put again as the same
    /// bytes through the same rules (the roller keeps finished months, any other device its open months only, never more than the quota, nothing
    /// while an older app is in the group, at most 40 requests a pass, a 429 ends the pass); a month that rolled up and was removed is never put again.
    /// On Drive the kept months are put again as the same version, and their tracking is left alone (a month that rolled up is not).
    /// </summary>
    public async Task ResendKeptAsync(string transport)
    {
        if (_key() is not SyncKey key)
        {
            return;
        }

        GoalHistoryTransportState state = StateFor(key).Transport(transport);
        if (transport == "relay")
        {
            // The relay's own files are put again as the very same bytes (the 25-day re-upload path): forgetting when they were uploaded is enough.
            state.Uploaded.Clear();
        }
        else
        {
            // Drive keeps files until they are removed: the kept months are put again as the very same version (same bytes, same WrittenAt) by the next
            // pass. What this PC wrote stays recorded, so a finished month that waits for its removal still goes later, and nothing is read as new.
            foreach (string month in state.Written.Keys.Where(month => !state.Removed.Contains(month)))
            {
                state.ResendMonths.Add(month);
            }
        }

        await _stateStore.SaveAsync();
    }

    /// <summary>The year files this PC has merged, for its regular sync file (<c>goalYearsMerged</c>); null for none.</summary>
    public IReadOnlyList<SyncGoalYearMerged>? Marker => _stateStore.Value.OwnMarker.Count == 0 ? null : _stateStore.Value.OwnMarker.ToList();

    /// <summary>The ids of the goals whose tombstone this PC pruned (Q649), kept for good.</summary>
    public IReadOnlySet<Guid> PrunedGoals => _stateStore.Value.PrunedGoals;

    /// <summary>Every name this PC put on the transport: they go when it leaves the group (the relay) or when the key changes.</summary>
    public IReadOnlyList<string> OwnNames(string transport)
    {
        return _stateStore.Value.Transports.TryGetValue(transport, out GoalHistoryTransportState? state) ? state.OwnNames.ToList() : [];
    }

    /// <summary>
    /// A regular sync file with goal parts merged here (<see cref="GoalListSync.ReceiveAsync"/>): the device is syncing (its last complete
    /// merge and its last regular merge are now), this PC is no longer new to the group, and the device's "merged year files" marker is
    /// kept (the copy of its file kept for display has none).
    /// </summary>
    public void NoteRegularFile(SyncFile file)
    {
        if (_key() is not SyncKey key)
        {
            return;
        }

        GoalHistoryState state = StateFor(key);
        DateTimeOffset now = _time.GetUtcNow();
        Guid device = file.Device.DeviceId;
        bool changed = !state.GoalsPartMerged || !state.LastRegularMerge.ContainsKey(device);
        state.GoalsPartMerged = true;
        state.LastRegularMerge[device] = now;
        state.LastMergeFrom[device] = now;
        List<SyncGoalYearMerged>? marker = file.GoalYearsMerged?.Where(item => item is not null).ToList();
        string before = MarkerKey(state.DeviceMarkers.GetValueOrDefault(device));
        if (marker is not null)
        {
            state.DeviceMarkers[device] = marker;
        }
        else
        {
            state.DeviceMarkers.Remove(device);
        }

        // The times are kept in memory and saved with the next pass; a new device or a changed marker is saved now.
        if (changed || before != MarkerKey(marker))
        {
            _ = _stateStore.SaveAsync();
        }
    }

    private static string MarkerKey(IEnumerable<SyncGoalYearMerged>? marker)
    {
        return marker is null ? "-" : string.Join(";", marker.Select(item => $"{item.Year}/{item.RolledBy:N}/{item.RolledAt.UtcTicks}"));
    }

    /// <summary>
    /// Whether <paramref name="name"/> is the name of a goal history file this PC knows (with <paramref name="extension"/>): the relay's
    /// regular reader leaves such a file alone without downloading it.
    /// </summary>
    public bool IsHistoryName(string name, string extension)
    {
        if (_key() is not SyncKey key)
        {
            return false;
        }

        int lastYear = _time.GetUtcNow().UtcDateTime.Year + 1;
        List<Guid> ids = KnownDevices(_stateStore.Value).OrderBy(id => id).ToList();
        string cacheKey = key.GroupId + extension + lastYear + string.Join(",", ids);
        if (cacheKey != _namesKey || _names is null)
        {
            _names = GoalHistoryNames.Candidates(key.GroupId, extension, ids, lastYear).Keys.ToHashSet(StringComparer.Ordinal);
            _namesKey = cacheKey;
        }

        return _names.Contains(name);
    }

    private HashSet<Guid> KnownDevices(GoalHistoryState state)
    {
        var devices = new HashSet<Guid>();
        if (_self() is Guid me)
        {
            devices.Add(me);
        }

        devices.UnionWith(_devices().Select(device => device.Id));
        devices.UnionWith(state.DeviceMarkers.Keys);
        devices.UnionWith(state.LastRegularMerge.Keys);
        devices.UnionWith(state.LastMergeFrom.Keys);
        return devices;
    }

    /// <summary>Whether a pass over <paramref name="transport"/> is due (<see cref="RunEvery"/>).</summary>
    public bool Due(string transport)
    {
        return !_lastRun.TryGetValue(transport, out DateTimeOffset last) || _time.GetUtcNow() - last >= RunEvery;
    }

    /// <summary>A pass when one is due; nothing otherwise (no network).</summary>
    public Task<GoalHistoryRun> RunIfDueAsync(IGoalHistoryStore store, CancellationToken cancellationToken)
    {
        return Due(store.Transport) ? RunAsync(store, cancellationToken) : Task.FromResult(new GoalHistoryRun());
    }

    /// <summary>One pass over the transport (see the class summary). Transport errors are the caller's to catch; what was done before one stays recorded.</summary>
    public async Task<GoalHistoryRun> RunAsync(IGoalHistoryStore store, CancellationToken cancellationToken)
    {
        var run = new GoalHistoryRun();
        if (!_enabled() || _self() is not Guid me || me == Guid.Empty || _key() is not SyncKey key || !_goals.Store.IsReady)
        {
            return run;
        }

        await _gate.WaitAsync(cancellationToken);
        GoalHistoryState? state = null;
        string markerText = string.Empty;
        try
        {
            run.Ran = true;
            _lastRun[store.Transport] = _time.GetUtcNow();
            state = StateFor(key);
            markerText = MarkerText(state);
            var pass = new Pass(store, state, state.Transport(store.Transport), me, key, RelayGated(store));

            pass.Spend();
            await ListAsync(pass, cancellationToken);
            await DeleteStaleNamesAsync(pass, cancellationToken);
            await ReadAsync(pass, run, cancellationToken);
            if (!pass.Stopped && await RollAsync(pass, run, cancellationToken) && pass.Spend())
            {
                await ListAsync(pass, cancellationToken);
                await ReadAsync(pass, run, cancellationToken);
            }

            if (!pass.Stopped)
            {
                await WriteAsync(pass, run, cancellationToken);
            }

            if (!pass.Stopped)
            {
                await RemoveAsync(pass, run, cancellationToken);
            }

            if (!pass.Stopped)
            {
                await ReuploadYearsAsync(pass, run, cancellationToken);
            }

            run.Capped = pass.Capped;
        }
        finally
        {
            await _stateStore.SaveAsync();
            _gate.Release();
            if (state is not null && MarkerText(state) != markerText)
            {
                MarkerChanged?.Invoke(this, EventArgs.Empty);
            }

            if (run.Ran)
            {
                _log($"goal history {store.Transport}: {run}");
            }
        }

        return run;
    }

    /// <summary>
    /// This PC has left its group (no key): the history files it put in a store that deletes (Drive) go, with its state of that transport
    /// (the relay's go with its sync file, <see cref="OwnNames"/>). Best effort: the caller catches transport errors.
    /// </summary>
    public async Task LeaveAsync(IGoalHistoryStore store, CancellationToken cancellationToken)
    {
        if (!store.CanDelete || _key() is not null)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            GoalHistoryState state = _stateStore.Value;
            if (!state.Transports.TryGetValue(store.Transport, out GoalHistoryTransportState? transport) || transport.OwnNames.Count == 0)
            {
                return;
            }

            IReadOnlyList<GoalHistoryRemoteFile> listing = await store.ListAsync(cancellationToken);
            var listed = listing.Select(file => file.Name).ToHashSet(StringComparer.Ordinal);
            int deleted = 0;
            foreach (string name in transport.OwnNames.ToList())
            {
                if (listed.Contains(name))
                {
                    await store.DeleteAsync(name, cancellationToken);
                    deleted++;
                }
            }

            state.Transports.Remove(store.Transport);
            await _stateStore.SaveAsync();
            _log($"goal history {store.Transport}: left deleted={deleted}");
        }
        finally
        {
            _gate.Release();
        }
    }

    // ON THE RELAY: nothing is written while a device of the group (not removed) is on an app that doesn't know history files (Q740).
    private bool RelayGated(IGoalHistoryStore store)
    {
        return store.MaxFiles is not null && _devices().Where(info => !info.Removed).Any(info => !GoalHistoryRules.KnowsHistory(info.Kind, info.AppVersion));
    }

    // ---------------------------------------------------------------------------------------------------- state

    private GoalHistoryState StateFor(SyncKey key)
    {
        GoalHistoryState state = _stateStore.Value;
        string group = key.Fingerprint();
        if (state.Group == group)
        {
            return state;
        }

        // A different key (the group was rekeyed or joined again): everything is new. The files under the old key are of no use to the
        // new group; Drive's are deleted at the next pass, the relay's expire (its own group was deleted on leaving).
        Dictionary<string, List<string>> stale = state.StaleNames;
        if (state.Group.Length > 0)
        {
            foreach ((string transport, GoalHistoryTransportState old) in state.Transports)
            {
                if (old.OwnNames.Count > 0)
                {
                    stale[transport] = stale.GetValueOrDefault(transport, []).Concat(old.OwnNames).Distinct().ToList();
                }
            }
        }

        HashSet<Guid> pruned = state.PrunedGoals;
        HashSet<Guid> tombstoned = state.TombstonedGoals;
        _stateStore.Reset();
        state = _stateStore.Value;
        state.Group = group;
        state.PrunedGoals = pruned;
        state.TombstonedGoals = tombstoned;
        state.StaleNames = stale;
        return state;
    }

    // The old key's files, after the listing (a store deletes by what it listed). Kept in the saved state until they are gone; a store that
    // never deletes (the relay) lets them expire.
    private async Task DeleteStaleNamesAsync(Pass pass, CancellationToken cancellationToken)
    {
        if (!pass.State.StaleNames.TryGetValue(pass.Store.Transport, out List<string>? names))
        {
            return;
        }

        if (pass.Store.CanDelete)
        {
            foreach (string name in names.Where(pass.AllNames.Contains).ToList())
            {
                if (!pass.Spend())
                {
                    return;
                }

                await pass.Store.DeleteAsync(name, cancellationToken);
            }
        }

        pass.State.StaleNames.Remove(pass.Store.Transport);
    }

    private static string MarkerText(GoalHistoryState state) => MarkerKey(state.OwnMarker);

    // Goals whose tombstone went since the last look (Q649): kept, with the id only.
    private void TrackPrunedGoals()
    {
        if (!_goals.Store.IsReady)
        {
            return;
        }

        GoalSyncLists after = GoalStore.ToSync(_goals.Lists);
        GoalHistoryState state = _stateStore.Value;
        var heldIds = new HashSet<Guid>(after.Goals.Where(goal => goal is not null).Select(goal => goal.Id));
        foreach (GoalNewerItem raw in GoalNewerJson.NewerOf(after.Goals))
        {
            if (GoalRawItem.TryRead(raw.Json) is { } item)
            {
                heldIds.Add(item.Id);
            }
        }

        var pruned = new HashSet<Guid>();
        if (_previousGoals is not null)
        {
            pruned.UnionWith(GoalHistoryRules.PrunedGoalIds(_previousGoals, after));
        }

        // A tombstone seen in an earlier run of the app and gone now (pruned while the app was off).
        pruned.UnionWith(state.TombstonedGoals.Where(id => !heldIds.Contains(id)));
        var tombstoned = new HashSet<Guid>(after.Goals.Where(goal => goal is not null && !goal.IsLive).Select(goal => goal.Id));
        bool changed = !pruned.IsSubsetOf(state.PrunedGoals) || !tombstoned.SetEquals(state.TombstonedGoals);
        _previousGoals = after;
        if (changed)
        {
            state.PrunedGoals.UnionWith(pruned);
            state.TombstonedGoals = tombstoned;
            _ = _stateStore.SaveAsync();
        }
    }

    // ---------------------------------------------------------------------------------------------------- list

    private async Task ListAsync(Pass pass, CancellationToken cancellationToken)
    {
        IReadOnlyList<GoalHistoryRemoteFile> listing = await pass.Store.ListAsync(cancellationToken);
        DateTimeOffset now = _time.GetUtcNow();
        HashSet<Guid> devices = KnownDevices(pass.State);
        devices.Add(pass.Me);
        IReadOnlyDictionary<string, GoalHistoryFileKey> names = GoalHistoryNames.Candidates(pass.Key.GroupId, pass.Store.Extension, devices, now.UtcDateTime.Year + 1);
        pass.TotalFiles = listing.Count;
        pass.NewFiles = 0;
        pass.Remote.Clear();
        pass.History.Clear();
        pass.AllNames.Clear();
        foreach (GoalHistoryRemoteFile file in listing)
        {
            pass.AllNames.Add(file.Name);
            if (names.TryGetValue(file.Name, out GoalHistoryFileKey? key))
            {
                pass.Remote[key] = file;
                pass.History.Add(new GoalHistoryListing(key, file.ModifiedAt));
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------- read

    private async Task ReadAsync(Pass pass, GoalHistoryRun run, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _time.GetUtcNow();
        GoalHistoryState state = pass.State;
        GoalHistoryTransportState transport = pass.Transport;
        IReadOnlyList<GoalHistoryDeviceInfo> infos = _devices();
        var removed = infos.Where(info => info.Removed).Select(info => info.Id).ToHashSet();
        IReadOnlyDictionary<Guid, DateTimeOffset> lastMerge = GoalHistoryRules.ForgetRemoved(state.LastMergeFrom, removed);
        var lastRead = new Dictionary<GoalHistoryFileKey, DateTimeOffset>();
        foreach ((string text, DateTimeOffset at) in transport.Read)
        {
            if (GoalHistoryNames.ParseKey(text) is { } parsed)
            {
                lastRead[parsed] = at;
            }
        }

        // Rows read before any regular file's goals part has merged are all dropped (no goal is held yet): nothing is downloaded for them.
        // A PC that knows no other device has no goals part to wait for (its own files are all there is).
        bool goalsReady = state.GoalsPartMerged || !infos.Any(info => info.Id != pass.Me && !info.Removed);
        bool newToGroup = transport.NewToGroup || !goalsReady;
        IReadOnlyList<GoalHistoryListing> toRead = goalsReady
            ? GoalHistoryRules.ToRead(pass.History, lastRead, lastMerge, removed, pass.Me, newToGroup, now)
            : [];
        if (newToGroup && toRead.Count > 0)
        {
            // New to the group, a device reads every file once, its own too; one already read at its listed version is not downloaded again.
            HashSet<GoalHistoryFileKey> ordinary = GoalHistoryRules.ToRead(pass.History, lastRead, lastMerge, removed, pass.Me, false, now).Select(file => file.Key).ToHashSet();
            toRead = toRead.Where(file => ordinary.Contains(file.Key) || !lastRead.TryGetValue(file.Key, out DateTimeOffset read) || read != file.ModifiedAt).ToList();
        }

        HashSet<Guid> staleBefore = pass.History
            .Where(file => file.Key.Kind == GoalHistoryFileKind.Month && file.Key.DeviceId is Guid device && device != pass.Me && !removed.Contains(device))
            .Select(file => file.Key.DeviceId!.Value)
            .Where(device => !lastMerge.TryGetValue(device, out DateTimeOffset merged) || now - merged > GoalHistoryRules.FullReadAfter)
            .ToHashSet();

        var rows = new List<GoalHistoryRows>();
        var years = new Dictionary<GoalHistoryFileKey, GoalHistoryYear>();
        var skipped = new HashSet<GoalHistoryFileKey>();
        foreach (GoalHistoryListing file in toRead)
        {
            GoalHistoryRemoteFile remote = pass.Remote[file.Key];
            string text = GoalHistoryNames.KeyText(file.Key);
            if (remote.Tag is not null && transport.Read.ContainsKey(text) && transport.ReadTag.TryGetValue(text, out string? tag) && tag == remote.Tag)
            {
                // The same bytes as the ones merged before (the relay's etag): only its time moved (put again).
                transport.Read[text] = file.ModifiedAt;
                continue;
            }

            if (remote.Size > pass.Store.MaxFileBytes || (_unreadableTried.TryGetValue(remote.Name, out DateTimeOffset tried) && now - tried < RetryUnreadableAfter))
            {
                // Logged when it was found; it never holds a stale device's complete read or the "new to the group" state back.
                skipped.Add(file.Key);
                continue;
            }

            if (!pass.Spend())
            {
                break;
            }

            byte[]? bytes = await pass.Store.GetAsync(remote.Name, cancellationToken);
            if (bytes is null)
            {
                continue;
            }

            if (file.Key.Kind == GoalHistoryFileKind.Month)
            {
                GoalHistoryMonth? month = GoalHistoryCodec.OpenMonth(bytes, pass.Key);
                if (month is null || month.Key != file.Key)
                {
                    NoteUnreadable(remote, run, now);
                    skipped.Add(file.Key);
                    continue;
                }

                rows.Add(new GoalHistoryRows(file, month.Entries));
            }
            else
            {
                GoalHistoryYear? year = GoalHistoryCodec.OpenYear(bytes, pass.Key);
                if (year is null || year.Key != file.Key)
                {
                    NoteUnreadable(remote, run, now);
                    skipped.Add(file.Key);
                    continue;
                }

                rows.Add(new GoalHistoryRows(file, year.Entries));
                years[file.Key] = year;
            }
        }

        if (rows.Count > 0)
        {
            GoalHistoryMerged? merged = null;
            await _goals.ApplySyncedAsync(
                () =>
                {
                    if (_held() is not GoalSyncLists held)
                    {
                        return null;
                    }

                    var late = new GoalHistoryLatePrune(state.FirstDropAt, ActiveDevices(pass, now), pass.Me, state.LastRegularMerge);
                    merged = GoalHistoryRules.Merge(held, null, rows, now, state.PrunedGoals, late);
                    return merged.Lists;
                },
                this);
            if (merged is not null)
            {
                // A late joiner: goals no active device knows any more (all rows older than 30 days) are pruned goals; their files count as read.
                if (merged.NewlyPruned is { Count: > 0 } freed)
                {
                    state.PrunedGoals.UnionWith(freed);
                }

                state.FirstDropAt = merged.Dropped.Any(entry => !state.PrunedGoals.Contains(entry.GoalId)) ? state.FirstDropAt ?? now : null;
                foreach (GoalHistoryListing read in merged.Read)
                {
                    string text = GoalHistoryNames.KeyText(read.Key);
                    transport.Read[text] = read.ModifiedAt;
                    if (pass.Remote[read.Key].Tag is string tag)
                    {
                        transport.ReadTag[text] = tag;
                    }

                    lastRead[read.Key] = read.ModifiedAt;
                    run.Read++;
                    if (years.TryGetValue(read.Key, out GoalHistoryYear? year))
                    {
                        state.OwnMarker = GoalHistoryRules.Marked(state.OwnMarker, year).ToList();
                        KeepYear(transport, year);
                    }
                }

                TrackPrunedGoals();
                foreach (GoalHistoryListing read in merged.Read.Where(item => item.Key.Kind == GoalHistoryFileKind.Month && item.Key.DeviceId is Guid other && other != pass.Me))
                {
                    MonthMerged?.Invoke(read.Key.DeviceId!.Value, GoalHistoryNames.MonthText(read.Key.Year, read.Key.Month!.Value), read.ModifiedAt);
                }
            }
        }

        // A stale device whose files are all read now has a complete merge: it is not stale again until 90 days pass. A file that is too big
        // or can't be opened is logged and left out of that.
        foreach (Guid device in staleBefore)
        {
            bool complete = pass.History
                .Where(file => file.Key.Kind == GoalHistoryFileKind.Month && file.Key.DeviceId == device && !skipped.Contains(file.Key))
                .All(file => transport.Read.TryGetValue(GoalHistoryNames.KeyText(file.Key), out DateTimeOffset at) && at == file.ModifiedAt);
            if (complete)
            {
                state.LastMergeFrom[device] = now;
            }
        }

        // No longer new to the group once a regular goals part has merged and every listed file is read or left out.
        if (transport.NewToGroup && goalsReady && !pass.Stopped && GoalHistoryRules.AllAccountedFor(pass.History, lastRead, skipped))
        {
            transport.NewToGroup = false;
        }
    }

    private void NoteUnreadable(GoalHistoryRemoteFile remote, GoalHistoryRun run, DateTimeOffset now)
    {
        _unreadableTried[remote.Name] = now;
        run.Unreadable++;
    }

    private static void KeepYear(GoalHistoryTransportState transport, GoalHistoryYear year)
    {
        string text = GoalHistoryNames.KeyText(year.Key);
        if (!transport.Years.TryGetValue(text, out GoalHistoryYear? kept) || year.RolledAt >= kept.RolledAt)
        {
            transport.Years[text] = year with { Entries = [] };
        }
    }

    // ---------------------------------------------------------------------------------------------------- roll up

    private async Task<bool> RollAsync(Pass pass, GoalHistoryRun run, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _time.GetUtcNow();
        IReadOnlyList<GoalHistoryDevice> active = ActiveDevices(pass, now);
        if (GoalHistoryRules.Roller(active) != pass.Me)
        {
            return false;
        }

        bool rolled = false;
        IEnumerable<int> years = pass.History
            .Where(file => file.Key.Kind == GoalHistoryFileKind.Month && GoalHistoryRules.RollDue(file.Key.Year, now))
            .Select(file => file.Key.Year)
            .Distinct()
            .Order()
            .ToList();
        foreach (int year in years)
        {
            List<GoalHistoryListing> monthFiles = pass.History.Where(file => file.Key.Kind == GoalHistoryFileKind.Month && file.Key.Year == year).ToList();
            if (pass.Transport.RollChecked.TryGetValue(year, out DateTimeOffset checkedAt) && now - checkedAt < RollEvery && monthFiles.All(file => file.ModifiedAt <= checkedAt))
            {
                continue;
            }

            if (pass.Gated)
            {
                run.Gated++;
                continue;
            }

            GoalHistoryYear? existing = null;
            GoalHistoryListing? yearFile = pass.History
                .Where(file => file.Key.Kind == GoalHistoryFileKind.Year && file.Key.Year == year)
                .OrderByDescending(file => file.Key.DeviceId == pass.Me)
                .ThenByDescending(file => file.ModifiedAt)
                .FirstOrDefault();
            if (yearFile is not null)
            {
                if (pass.Remote[yearFile.Key].Size <= pass.Store.MaxFileBytes && !pass.Spend())
                {
                    return rolled;
                }

                byte[]? bytes = pass.Remote[yearFile.Key].Size > pass.Store.MaxFileBytes ? null : await pass.Store.GetAsync(pass.Remote[yearFile.Key].Name, cancellationToken);
                existing = bytes is null ? null : GoalHistoryCodec.OpenYear(bytes, pass.Key);
                if (existing is null || existing.Key != yearFile.Key)
                {
                    // A year file that is listed and can't be read is never rolled over.
                    run.Unreadable++;
                    continue;
                }

                KeepYear(pass.Transport, existing);
            }

            var months = new List<GoalHistoryMonth>();
            foreach (GoalHistoryListing file in monthFiles)
            {
                if (file.Key.DeviceId == pass.Me && pass.Transport.Written.TryGetValue(GoalHistoryNames.MonthText(file.Key.Year, file.Key.Month ?? 0), out GoalHistoryMonth? own))
                {
                    months.Add(own);
                    continue;
                }

                GoalHistoryRemoteFile remote = pass.Remote[file.Key];
                if (remote.Size <= pass.Store.MaxFileBytes && !pass.Spend())
                {
                    return rolled;
                }

                byte[]? bytes = remote.Size > pass.Store.MaxFileBytes ? null : await pass.Store.GetAsync(remote.Name, cancellationToken);
                GoalHistoryMonth? month = bytes is null ? null : GoalHistoryCodec.OpenMonth(bytes, pass.Key);
                if (month is not null && month.Key == file.Key)
                {
                    months.Add(month);
                }
            }

            pass.Transport.RollChecked[year] = now;
            GoalHistoryYear? rolledYear = GoalHistoryRules.RollUp(existing, months, year, pass.Me, pass.History, now);
            if (rolledYear is null)
            {
                continue;
            }

            byte[] sealedYear = GoalHistoryCodec.Seal(rolledYear, pass.Key);
            string name = GoalHistoryNames.NameFor(pass.Key.GroupId, rolledYear.Key, pass.Store.Extension);
            if (!Fits(pass, run, sealedYear.Length, name))
            {
                continue;
            }

            if (!await PutAsync(pass, run, name, sealedYear, cancellationToken))
            {
                return rolled;
            }

            pass.NewFiles++;
            pass.Transport.OwnNames.Add(name);
            pass.Transport.OwnYearNames.Add(name);
            pass.Transport.Uploaded[name] = now;
            KeepYear(pass.Transport, rolledYear);
            run.Rolled++;
            rolled = true;
        }

        return rolled;
    }

    private IReadOnlyList<GoalHistoryDevice> ActiveDevices(Pass pass, DateTimeOffset now)
    {
        var group = new List<GoalGroupDevice>();
        foreach (GoalHistoryDeviceInfo info in _devices())
        {
            // Only a device whose regular file WITH goal parts merged here counts: an app that doesn't sync goals never merges history.
            DateTimeOffset? merged = pass.State.LastRegularMerge.TryGetValue(info.Id, out DateTimeOffset at) ? at : null;
            group.Add(new GoalGroupDevice(
                info.Id, info.IsPc, info.Removed, merged, pass.State.DeviceMarkers.TryGetValue(info.Id, out List<SyncGoalYearMerged>? marker) ? marker : null));
        }

        group.Add(new GoalGroupDevice(pass.Me, true, false, now, pass.State.OwnMarker));
        return GoalHistoryRules.ActiveDevices(group, pass.Me, now);
    }

    // ---------------------------------------------------------------------------------------------------- write

    private async Task WriteAsync(Pass pass, GoalHistoryRun run, CancellationToken cancellationToken)
    {
        if (_held() is not GoalSyncLists held)
        {
            return;
        }

        DateTimeOffset now = _time.GetUtcNow();
        GoalHistoryTransportState transport = pass.Transport;
        ForgetVanishedYears(pass);
        var months = new SortedSet<(int Year, int Month)>(GoalHistoryRules.MonthsOf(held.Entries));
        foreach (string text in transport.Written.Keys)
        {
            string[] parts = text.Split('-');
            if (parts.Length == 2 && int.TryParse(parts[0], out int year) && int.TryParse(parts[1], out int month))
            {
                months.Add((year, month));
            }
        }

        // ON THE RELAY: only the roller keeps finished months, a device at most its quota, newest first (GoalHistoryRules.RelayKeep).
        HashSet<(int Year, int Month)>? keep = pass.Store.MaxFiles is null ? null : RelayMonths(pass, months, now);
        foreach ((int year, int month) in months.Reverse())
        {
            if (pass.Stopped)
            {
                return;
            }

            if (keep is not null && !keep.Contains((year, month)))
            {
                continue;
            }

            string monthText = GoalHistoryNames.MonthText(year, month);
            transport.Written.TryGetValue(monthText, out GoalHistoryMonth? last);
            GoalHistoryMonth? fresh = GoalHistoryRules.MonthToWrite(last, held.Entries, pass.Me, year, month, now);
            GoalHistoryMonth? toPut = fresh;
            string name = GoalHistoryNames.NameFor(pass.Key.GroupId, GoalHistoryFileKey.ForMonth(pass.Me, year, month), pass.Store.Extension);
            bool listed = pass.Remote.Values.Any(file => file.Name == name);
            bool reupload = false;
            bool sameBytes = false;
            if (toPut is null && last is not null && !transport.Removed.Contains(monthText))
            {
                // The same content: put again only when the file is missing here, or the store would expire it.
                bool missing = !listed;
                bool aging = pass.Store.Expires && (!transport.Uploaded.TryGetValue(name, out DateTimeOffset uploaded) || now - uploaded >= ReuploadEvery);
                bool resend = transport.ResendMonths.Contains(monthText);
                if (missing || aging || resend)
                {
                    toPut = last;
                    reupload = true;
                    sameBytes = !missing;
                }
            }

            if (toPut is null)
            {
                continue;
            }

            if (pass.Gated)
            {
                // A device of the group is on an app that would take the file for an unreadable sync file.
                run.Gated++;
                continue;
            }

            byte[]? sealedMonth = null;
            if (sameBytes)
            {
                // Like a year file: the very same bytes, so the relay's etag doesn't change and no reader downloads it again.
                if (!pass.Spend())
                {
                    return;
                }

                byte[]? current = await pass.Store.GetAsync(name, cancellationToken);
                if (current is not null && GoalHistoryCodec.OpenMonth(current, pass.Key) is { } opened && opened.Key == toPut.Key)
                {
                    sealedMonth = current;
                }
            }

            sealedMonth ??= GoalHistoryCodec.Seal(toPut, pass.Key);
            if (!Fits(pass, run, sealedMonth.Length, name, listed))
            {
                continue;
            }

            if (!await PutAsync(pass, run, name, sealedMonth, cancellationToken))
            {
                return;
            }

            if (!listed)
            {
                pass.NewFiles++;
            }

            transport.Written[monthText] = toPut;
            transport.ResendMonths.Remove(monthText);
            transport.Uploaded[name] = now;
            transport.OwnNames.Add(name);
            transport.Removed.Remove(monthText);
            if (reupload)
            {
                run.Reuploaded++;
            }
            else
            {
                run.Written++;
            }
        }
    }

    // ON THE RELAY: the months this device keeps there now (GoalHistoryRules.RelayKeep with RelayQuota): the roller all, the others their
    // open months; at most the quota; a month already rolled up and removed is not a candidate.
    private HashSet<(int Year, int Month)> RelayMonths(Pass pass, IEnumerable<(int Year, int Month)> months, DateTimeOffset now)
    {
        var removed = _devices().Where(info => info.Removed).Select(info => info.Id).ToHashSet();
        bool roller = GoalHistoryRules.Roller(ActiveDevices(pass, now)) == pass.Me;
        int yearFiles = pass.History.Count(file => file.Key.Kind == GoalHistoryFileKind.Year);
        int devices = Math.Max(1, KnownDevices(pass.State).Count(id => !removed.Contains(id)));
        int quota = GoalHistoryRules.RelayQuota(yearFiles, devices);
        return GoalHistoryRules.RelayKeep(months.Where(month => !pass.Transport.Removed.Contains(GoalHistoryNames.MonthText(month.Year, month.Month))), roller, quota, now).ToHashSet();
    }

    // A year file this PC has seen that is no longer listed (on the relay: its roller was away for over 30 days): the months of that year it
    // had removed are put again and rolled again, and the year's old record goes so no month is removed on its account.
    private static void ForgetVanishedYears(Pass pass)
    {
        foreach (int year in pass.Transport.Years.Values.Select(item => item.Year).Distinct().ToList())
        {
            if (pass.History.Any(file => file.Key.Kind == GoalHistoryFileKind.Year && file.Key.Year == year))
            {
                continue;
            }

            foreach (string monthText in pass.Transport.Removed.Where(text => text.StartsWith($"{year:D4}-", StringComparison.Ordinal)).ToList())
            {
                pass.Transport.Removed.Remove(monthText);
            }

            foreach (string text in pass.Transport.Years.Where(pair => pair.Value.Year == year).Select(pair => pair.Key).ToList())
            {
                pass.Transport.Years.Remove(text);
            }

            pass.Transport.RollChecked.Remove(year);
        }
    }

    // One put. A 429 on the relay means the group is full (or the rate limit): the pass ends here, quietly, with no retry.
    private static async Task<bool> PutAsync(Pass pass, GoalHistoryRun run, string name, byte[] bytes, CancellationToken cancellationToken)
    {
        if (!pass.Spend())
        {
            return false;
        }

        try
        {
            await pass.Store.PutAsync(name, bytes, cancellationToken);
            return true;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            run.GroupFull++;
            pass.Stopped = true;
            return false;
        }
    }

    // The store's limits: a file over its size is not written; a last guard keeps a few places of the relay's group free.
    private bool Fits(Pass pass, GoalHistoryRun run, int length, string name, bool listed = false)
    {
        if (length > pass.Store.MaxFileBytes)
        {
            run.TooBig++;
            return false;
        }

        bool isNew = !listed && !pass.Remote.Values.Any(file => file.Name == name);
        if (isNew && pass.Store.MaxFiles is int limit && pass.TotalFiles + pass.NewFiles + 1 > limit - RelayHeadroom)
        {
            run.GroupFull++;
            return false;
        }

        return true;
    }

    // ---------------------------------------------------------------------------------------------------- remove

    private async Task RemoveAsync(Pass pass, GoalHistoryRun run, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _time.GetUtcNow();
        GoalHistoryTransportState transport = pass.Transport;
        IReadOnlyList<GoalHistoryDevice> active = ActiveDevices(pass, now);
        foreach ((string monthText, GoalHistoryMonth written) in transport.Written.ToList())
        {
            if (transport.Removed.Contains(monthText))
            {
                continue;
            }

            GoalHistoryFileKey monthKey = written.Key;
            string name = GoalHistoryNames.NameFor(pass.Key.GroupId, monthKey, pass.Store.Extension);
            if (!MayRemove(transport, monthKey, written.WrittenAt, active, now))
            {
                continue;
            }

            if (pass.Store.CanDelete)
            {
                if (!pass.Spend())
                {
                    return;
                }

                await pass.Store.DeleteAsync(name, cancellationToken);
                transport.Uploaded.Remove(name);
                transport.OwnNames.Remove(name);
            }

            // On the relay the file is just not put again; it expires.
            transport.Removed.Add(monthText);
            transport.ResendMonths.Remove(monthText);
            run.Removed++;
        }
    }

    private static bool MayRemove(GoalHistoryTransportState transport, GoalHistoryFileKey monthKey, DateTimeOffset writtenAt, IReadOnlyList<GoalHistoryDevice> active, DateTimeOffset now)
    {
        return transport.Years.Values
            .Where(year => year.Year == monthKey.Year)
            .Any(year => GoalHistoryRules.MayRemoveMonth(monthKey, writtenAt, year, active, now));
    }

    // ---------------------------------------------------------------------------------------------------- put again

    // The year files this PC rolled: put again as they are (the same bytes) before the relay would expire them.
    private async Task ReuploadYearsAsync(Pass pass, GoalHistoryRun run, CancellationToken cancellationToken)
    {
        if (!pass.Store.Expires)
        {
            return;
        }

        DateTimeOffset now = _time.GetUtcNow();
        foreach (string name in pass.Transport.OwnYearNames.ToList())
        {
            if (pass.Transport.Uploaded.TryGetValue(name, out DateTimeOffset uploaded) && now - uploaded < ReuploadEvery)
            {
                continue;
            }

            if (pass.Gated)
            {
                run.Gated++;
                continue;
            }

            if (!pass.Spend())
            {
                return;
            }

            byte[]? bytes = await pass.Store.GetAsync(name, cancellationToken);
            if (bytes is null)
            {
                // Gone (expired): the next roll-up writes it again if its month files are still here.
                pass.Transport.OwnYearNames.Remove(name);
                pass.Transport.OwnNames.Remove(name);
                pass.Transport.Uploaded.Remove(name);
                pass.Transport.RollChecked.Clear();
                continue;
            }

            if (!await PutAsync(pass, run, name, bytes, cancellationToken))
            {
                return;
            }

            pass.Transport.Uploaded[name] = now;
            run.Reuploaded++;
        }
    }

    private sealed class Pass(IGoalHistoryStore store, GoalHistoryState state, GoalHistoryTransportState transport, Guid me, SyncKey key, bool gated)
    {
        private int _requests;

        public IGoalHistoryStore Store { get; } = store;

        public GoalHistoryState State { get; } = state;

        public GoalHistoryTransportState Transport { get; } = transport;

        public Guid Me { get; } = me;

        public SyncKey Key { get; } = key;

        /// <summary>ON THE RELAY: a device of the group is on an app that doesn't know history files, so nothing is written (Q740).</summary>
        public bool Gated { get; } = gated;

        /// <summary>The history files the listing holds, by key.</summary>
        public Dictionary<GoalHistoryFileKey, GoalHistoryRemoteFile> Remote { get; } = [];

        public List<GoalHistoryListing> History { get; } = [];

        /// <summary>Every name the listing holds (history files and others).</summary>
        public HashSet<string> AllNames { get; } = new(StringComparer.Ordinal);

        public int TotalFiles { get; set; }

        /// <summary>Files this pass added to the store (they count toward its file limit).</summary>
        public int NewFiles { get; set; }

        /// <summary>The pass ends here: the request cap was reached, or the relay answered 429 to a put.</summary>
        public bool Stopped { get; set; }

        /// <summary>The pass ended on the request cap (not on a 429).</summary>
        public bool Capped { get; private set; }

        /// <summary>Counts one request; false (and the pass is over) when the store's cap per pass is used up.</summary>
        public bool Spend()
        {
            if (Stopped)
            {
                return false;
            }

            if (Store.MaxRequestsPerPass is int cap && _requests >= cap)
            {
                Stopped = true;
                Capped = true;
                return false;
            }

            _requests++;
            return true;
        }
    }
}
