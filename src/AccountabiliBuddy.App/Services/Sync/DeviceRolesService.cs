using System.Globalization;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// The PC's side of the Android device roles (ADR 90): main phone, tablet, other device. It keeps the merged role list this PC writes into its
/// sync file (<see cref="ForSync"/>), takes in the lists of received files (<see cref="Receive"/>), and holds a change back until it may apply.
/// <list type="bullet">
/// <item>What the readers use is the APPLIED list, never the merged one (<see cref="ThePhones"/>): a PC that receives a list keeps its old sleep
/// inputs (the alarm, bedtime and wake estimate) until the wait is over.</item>
/// <item>A change is detected by the effective roles (<see cref="SyncDeviceRoles.Effective"/> of the applied list against the merged one), for every
/// way it can come: an edit here, a received list, a removal (ADR 54), a device that left. Never by comparing entries or stamps.</item>
/// <item>Every change THIS PC asks for waits for its own upload (<see cref="SyncDeviceRoleGate"/>, read from the send log, with the CONFIGURED sync
/// methods): the owner's gate, in both modes. A change of "the phones" (the devices behind the PC's sleep inputs) also waits the looser-change
/// delay in AccountabiliBuddy mode, counted from the gate's opening (a received or derived change: from the first time it was seen), never applies
/// while a session runs, and has no "apply now". In ProductaBuddy mode it applies at once between sessions once the gate is open.</item>
/// <item>Cancel (<see cref="CancelAsync"/>) writes the applied roles again with <see cref="SyncDeviceRoles.Restore"/>.</item>
/// <item>A role is never read by a guard; this service touches no guard.</item>
/// </list>
/// App-owned document "sync.deviceRoles" (device-local). Use from the UI thread only.
/// </summary>
public sealed class DeviceRolesService
{
    /// <summary>The document key.</summary>
    public const string DocumentKey = "sync.deviceRoles";

    /// <summary>Why a role can't be changed while a focus session the user started is seen (no escape hatches).</summary>
    public const string LockedReason = "Roles can't change during a focus session.";

    /// <summary>How long a row says "Applied" after its roles took effect.</summary>
    public static readonly TimeSpan AppliedShownFor = TimeSpan.FromMinutes(5);

    private readonly SqliteJsonDocumentStore _store;
    private DeviceRolesDocument _document = DeviceRolesDocument.Empty;
    private Task _lastSave = Task.CompletedTask;
    private string _lastFingerprint = string.Empty;
    private DateTimeOffset? _lastAppliedAt;
    private HashSet<Guid> _lastAppliedIds = [];

    public DeviceRolesService(SqliteJsonDocumentStore store)
    {
        _store = store;
    }

    /// <summary>This PC's trusted time.</summary>
    public required Func<DateTimeOffset> Now { get; init; }

    /// <summary>This PC's device id (the writer of its entries).</summary>
    public required Func<Guid> SelfId { get; init; }

    /// <summary>The devices still in the group whose files this PC holds.</summary>
    public required Func<IReadOnlyList<ReceivedDevice>> Active { get; init; }

    /// <summary>This PC is in a device group.</summary>
    public Func<bool> InGroup { get; init; } = static () => true;

    /// <summary>The ids of the removed devices (ADR 54): their entries are dropped.</summary>
    public Func<IReadOnlySet<Guid>> RemovedIds { get; init; } = static () => new HashSet<Guid>();

    /// <summary>Why an edit is refused right now (a session the user started is seen: the Settings lock); null when it is allowed.</summary>
    public Func<string?> EditLockedReason { get; init; } = static () => null;

    /// <summary>A session runs (or another device's does): a change of the phones never applies now.</summary>
    public Func<bool> SessionActive { get; init; } = static () => false;

    /// <summary>ProductaBuddy mode: a change of the phones applies at once between sessions, with no delay.</summary>
    public Func<bool> IsCompanion { get; init; } = static () => false;

    /// <summary>The looser-change delay (10 minutes by default).</summary>
    public Func<TimeSpan> LooserWait { get; init; } = static () => TimeSpan.FromMinutes(10);

    /// <summary>This PC's own puts: per method, the newest write time that reached it (<see cref="SyncSendLog.NewestPutByMethod"/>).</summary>
    public Func<IReadOnlyDictionary<string, DateTimeOffset>> NewestPuts { get; init; } = static () => new Dictionary<string, DateTimeOffset>();

    /// <summary>The sync methods this PC has switched ON (its card's list), not the ones that currently work.</summary>
    public Func<IReadOnlyCollection<string>> Methods { get; init; } = static () => [];

    /// <summary>The zone the times in the status line are shown in.</summary>
    public Func<TimeZoneInfo> Zone { get; init; } = static () => TimeZoneInfo.Local;

    /// <summary>Raised when what the readers use, or the status of a waiting change, changed (the rows and the readers follow).</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when the merged list changed (an edit, a cancel, a received list, a removal): this PC's own file says something new and is written again.</summary>
    public event EventHandler? HeldChanged;

    /// <summary>A change this PC is holding back: its wait is running or it waits for the gate.</summary>
    public DeviceRolesPending? Pending => _document.Pending;

    /// <summary>This PC asked for the waiting change itself, so it can be cancelled (a received change is only waited out).</summary>
    public bool CanCancel => _document.Pending?.RequestedAt is not null;

    public void LoadBlocking()
    {
        try
        {
            DeviceRolesDocument? stored = Task.Run(() => _store.LoadAsync<DeviceRolesDocument>(DocumentKey)).GetAwaiter().GetResult();
            // A PC that has never applied a list starts from "every Android device has no role" (today's behaviour), so the first list it
            // receives is a change like any other and waits.
            _document = stored is { Held: not null, Applied: not null, AppliedIds: not null } ? stored : DeviceRolesDocument.Empty with { AppliedIds = AndroidIds() };
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            _document = DeviceRolesDocument.Empty with { AppliedIds = AndroidIds() };
        }
    }

    /// <summary>The merged list for this PC's sync file (the part is not written when null).</summary>
    public IReadOnlyList<SyncDeviceRole>? ForSync() => _document.Held.Count == 0 ? null : _document.Held;

    /// <summary>The list the readers use, for the checks.</summary>
    public IReadOnlyList<SyncDeviceRole> AppliedList => _document.Applied;

    /// <summary>
    /// Takes in the list of a received file (ADR 90): merged with the stamps rule and the 2-minute future filter on what arrives. It applies only
    /// through <see cref="TickAsync"/>'s rules, so a PC keeps its old sleep inputs until the wait is over.
    /// </summary>
    public void Receive(IReadOnlyList<SyncDeviceRole>? incoming)
    {
        if (incoming is null || incoming.Count == 0)
        {
            return;
        }

        List<SyncDeviceRole> merged = SyncDeviceRoles.Merge(_document.Held, incoming, Now(), RemovedIds()) ?? [];
        SetHeld(merged);
        Evaluate();
    }

    /// <summary>Applies a waiting change once the gate is open, the wait is over and no session runs; also takes removals. Runs every second.</summary>
    public Task TickAsync()
    {
        Evaluate();
        return _lastSave;
    }

    /// <summary>
    /// Gives an Android device a role (main phone, tablet, other device). Setting a main also writes the previous main as other. The change is
    /// written into the sync file at once and applied here later (see the class text).
    /// </summary>
    /// <returns>Null when asked; otherwise why not.</returns>
    public async Task<string?> SetRoleAsync(Guid device, string role)
    {
        if (EditLockedReason() is string locked)
        {
            return locked;
        }

        if (!InGroup())
        {
            return "This PC isn't linked to a device group.";
        }

        if (!AndroidIds().Contains(device))
        {
            return "Roles are for the Android devices of your group.";
        }

        Evaluate();
        if (_document.Held.Any(entry => entry.Id == device && entry.Role == role))
        {
            return null; // Already what the list says: nothing to write.
        }

        DateTimeOffset now = Now();
        List<SyncDeviceRole> withRole = SyncDeviceRoles.With(_document.Held, device, role, now, SelfId());
        List<SyncDeviceRole> merged = SyncDeviceRoles.Merge(withRole, null, now, RemovedIds()) ?? [];
        DateTimeOffset requested = merged.First(entry => entry.Id == device).At;
        // The devices this request wrote (the new main and the main it demoted): a Cancel puts back these and nothing else.
        Dictionary<Guid, string> was = _document.Held.ToDictionary(entry => entry.Id, entry => entry.Role);
        IEnumerable<Guid> written = merged.Where(entry => !was.TryGetValue(entry.Id, out string? role) || role != entry.Role).Select(entry => entry.Id);
        List<Guid> touched = written.Concat(OwnRequestAlive(_document.Pending) ? _document.Pending!.Touched ?? [] : []).Distinct().ToList();
        _document = _document with { Pending = new DeviceRolesPending(string.Empty, now, RequestedAt: requested, Touched: touched) };
        SetHeld(merged);
        Evaluate();
        await _lastSave;
        return null;
    }

    /// <summary>
    /// Cancels the change this PC asked for while it waits: the applied roles are written again (<see cref="SyncDeviceRoles.Restore"/>), with
    /// stamps past the entries they replace, so every device drops the change. Stricter, so never refused.
    /// </summary>
    public async Task CancelAsync()
    {
        if (!CanCancel)
        {
            return;
        }

        // Only the entries this PC's request wrote go back: the rest of the list (another device's change that merged in meanwhile) stays.
        IReadOnlyList<Guid>? touched = _document.Pending!.Touched;
        IEnumerable<SyncDeviceRole> previous = touched is null
            ? _document.Applied
            : _document.Held.Where(entry => !touched.Contains(entry.Id)).Concat(_document.Applied.Where(entry => touched.Contains(entry.Id)));
        List<SyncDeviceRole> restored = SyncDeviceRoles.Restore(_document.Held, previous, Now(), SelfId());
        SetHeld(SyncDeviceRoles.Merge(restored, null, Now(), RemovedIds()) ?? []);
        Evaluate();
        await _lastSave;
    }

    /// <summary>This PC left its group (or joined another): the roles were the old group's.</summary>
    public async Task ForgetAsync()
    {
        _document = DeviceRolesDocument.Empty;
        _lastAppliedAt = null;
        _lastAppliedIds = [];
        Save();
        Raise();
        await _lastSave;
    }

    /// <summary>
    /// "The phones" among <paramref name="devices"/>: the main phone when one is set, else every device with no role; tablets and other devices
    /// are left out. From the APPLIED roles. A device that is not an Android device (a PC, a watch) passes through untouched.
    /// </summary>
    public IReadOnlyList<ReceivedDevice> ThePhones(IReadOnlyList<ReceivedDevice> devices)
    {
        IReadOnlyDictionary<Guid, SyncDeviceRoleKind> effective = ReaderMap(devices);
        IReadOnlySet<Guid> phones = SyncDeviceRoles.ThePhones(effective);
        return devices
            .Where(device => device.File.Device.Kind != SyncDeviceCard.KindPhone || phones.Contains(device.File.Device.DeviceId))
            .ToList();
    }

    /// <summary>The role in use for an Android device (from the applied roles): Unassigned for a device with none.</summary>
    public SyncDeviceRoleKind KindOf(Guid device)
    {
        return ReaderMap(Active()).TryGetValue(device, out SyncDeviceRoleKind kind) ? kind : SyncDeviceRoleKind.Unassigned;
    }

    /// <summary>The role the merged list says the device has (what the row's menu marks as chosen, once the change applies).</summary>
    public SyncDeviceRoleKind ChosenKindOf(Guid device)
    {
        return SyncDeviceRoles.Effective(_document.Held, AndroidIds()).TryGetValue(device, out SyncDeviceRoleKind kind) ? kind : SyncDeviceRoleKind.Unassigned;
    }

    /// <summary>"Main phone", "Tablet" or "Other device" for the role in use; null for a device with no role ("Phone").</summary>
    public string? LabelOf(Guid device) => LabelOf(KindOf(device));

    /// <summary>How long a main phone may stay quiet before the Devices page says so (ADR 90: a reinstalled main phone is a new id, so the old one stays "main").</summary>
    public static readonly TimeSpan MainQuietFor = TimeSpan.FromDays(7);

    /// <summary>"This is the main phone, but it hasn't synced in 7 days." for a main phone whose newest file is older than <see cref="MainQuietFor"/>; null otherwise.</summary>
    public static string? QuietMainText(SyncDeviceRoleKind kind, TimeSpan sinceLastFile)
    {
        return kind == SyncDeviceRoleKind.Main && sinceLastFile > MainQuietFor ? "Main phone, but it hasn't synced in 7 days." : null;
    }

    /// <summary>The words for a role.</summary>
    public static string? LabelOf(SyncDeviceRoleKind kind) => kind switch
    {
        SyncDeviceRoleKind.Main => "Main phone",
        SyncDeviceRoleKind.Tablet => "Tablet",
        SyncDeviceRoleKind.Other => "Other device",
        _ => null,
    };

    /// <summary>The wire word for a role choice.</summary>
    public static string WordOf(SyncDeviceRoleKind kind) => kind switch
    {
        SyncDeviceRoleKind.Main => SyncDeviceRole.RoleMain,
        SyncDeviceRoleKind.Tablet => SyncDeviceRole.RoleTablet,
        _ => SyncDeviceRole.RoleOther,
    };

    /// <summary>
    /// What a row says about the waiting change, for the devices it moves: "Waiting to reach Google Drive…", "On Google Drive · applies at 4:15 PM",
    /// "Applies at 4:15 PM", "Applies when your session ends", then "Applied" for a few minutes. Null for any other device.
    /// </summary>
    public string? StatusOf(Guid device)
    {
        if (_document.Pending is { } pending && InvolvedIds().Contains(device))
        {
            return PendingText(pending);
        }

        return _lastAppliedAt is { } at && _lastAppliedIds.Contains(device) && Now() - at < AppliedShownFor ? "Applied" : null;
    }

    /// <summary>
    /// The migration assistant's "set up" check: every active Android device has a role in the merged list (a pending change counts), so the page
    /// is not offered again while its answer waits.
    /// </summary>
    public bool IsSetUp => AndroidIds() is { Count: > 0 } ids
        && SyncDeviceRoles.Effective(_document.Held, ids).Values.All(kind => kind != SyncDeviceRoleKind.Unassigned);

    /// <summary>How many Android devices are active in the group (the migration entry is offered from two).</summary>
    public int AndroidCount => AndroidIds().Count;

    /// <summary>The active Android devices, for the setup page and the Devices rows.</summary>
    public IReadOnlyList<ReceivedDevice> AndroidDevices() => Active().Where(device => device.File.Device.Kind == SyncDeviceCard.KindPhone).ToList();

    // ---- the evaluation ----------------------------------------------------------------------------------------------------------------

    private void Evaluate()
    {
        DateTimeOffset now = Now();
        IReadOnlySet<Guid> removed = RemovedIds();
        // Removed devices' entries go (ADR 54); a list nobody holds any more stays empty.
        List<SyncDeviceRole> held = SyncDeviceRoles.Merge(_document.Held, null, now, removed) ?? [];
        SetHeld(held);

        List<Guid> ids = AndroidIds();
        IReadOnlyDictionary<Guid, SyncDeviceRoleKind> before = SyncDeviceRoles.Effective(_document.Applied, _document.AppliedIds);
        IReadOnlyDictionary<Guid, SyncDeviceRoleKind> after = SyncDeviceRoles.Effective(held, ids);
        bool phones = SyncDeviceRoles.PhonesChanged(before, after);
        bool labels = LabelsDiffer(before, after);
        DeviceRolesPending? pending = _document.Pending;

        if (!phones && !labels)
        {
            // Nothing the readers or the rows show differs: take the list as it is (a device that only joined, one that left with no effect, a Cancel).
            if (pending is not null || !SameEntries(_document.Applied, held) || !_document.AppliedIds.SequenceEqual(ids))
            {
                _document = new DeviceRolesDocument(held, held.ToList(), ids, null);
                Save();
            }

            Raise();
            return;
        }

        string target = TargetOf(before, after);
        // A change that merges in while this PC's own request waits is a new target with a new wait, but the request (its Drive gate and its Cancel)
        // stays for as long as the list still holds this PC's entry.
        pending = pending is not null && (pending.Target.Length == 0 || pending.Target == target)
            ? pending with { Target = target }
            : pending is not null && OwnRequestAlive(pending)
                ? new DeviceRolesPending(target, now, pending.RequestedAt, Touched: pending.Touched)
                : new DeviceRolesPending(target, now);

        if (pending.ApplyAt is null)
        {
            pending = OpenGate(pending, phones, now);
        }

        bool due = pending.ApplyAt is { } applyAt
            && (!phones || (!SessionActive() && (IsCompanion() || now >= applyAt)));
        if (due)
        {
            _lastAppliedAt = now;
            _lastAppliedIds = InvolvedIds(before, after).ToHashSet();
            _document = new DeviceRolesDocument(held, held.ToList(), ids, null);
            Save();
            Raise();
            return;
        }

        if (pending != _document.Pending)
        {
            _document = _document with { Held = held, Pending = pending };
            Save();
        }

        Raise();
    }

    // The gate (ADR 90): this PC's own upload of the change, then, for a change of the phones, the delay from the gate's opening.
    private DeviceRolesPending OpenGate(DeviceRolesPending pending, bool phones, DateTimeOffset now)
    {
        DateTimeOffset? opened;
        if (pending.RequestedAt is { } requested)
        {
            SyncDeviceRoleGateState gate = SyncDeviceRoleGate.Evaluate(requested, NewestPuts(), Methods());
            opened = gate switch
            {
                SyncDeviceRoleGateState.Open => now,
                SyncDeviceRoleGateState.NothingToConfirm => now,
                _ => null,
            };
        }
        else
        {
            // A change this PC only received or derived has no upload to confirm: it waits from the first time it saw it.
            opened = pending.FirstSeen;
        }

        return opened is { } at ? pending with { GateOpenAt = at, ApplyAt = at + (phones ? LooserWait() : TimeSpan.Zero) } : pending;
    }

    private string PendingText(DeviceRolesPending pending)
    {
        bool phones = SyncDeviceRoles.PhonesChanged(
            SyncDeviceRoles.Effective(_document.Applied, _document.AppliedIds), SyncDeviceRoles.Effective(_document.Held, AndroidIds()));
        if (pending.ApplyAt is not { } applyAt)
        {
            return $"Waiting to reach {ReachWords()}…";
        }

        // "On Google Drive" only while the gate really is open: a method switched on or off since then never turns a guess into a claim.
        bool local = pending.RequestedAt is not null && SyncDeviceRoleGate.Evaluate(pending.RequestedAt.Value, NewestPuts(), Methods()) == SyncDeviceRoleGateState.Open;
        string prefix = local ? $"On {ReachWords()} · " : string.Empty;
        string applies;
        if (phones && SessionActive() && (IsCompanion() || Now() >= applyAt))
        {
            applies = "applies when your session ends";
        }
        else if (phones && !IsCompanion() && Now() < applyAt)
        {
            applies = $"applies at {ClockText(applyAt)}";
        }
        else
        {
            applies = "applies now";
        }

        string text = prefix + applies;
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private string ReachWords()
    {
        IReadOnlyCollection<string> methods = Methods();
        if (methods.Contains(SyncDeviceCard.MethodDrive))
        {
            return "Google Drive";
        }

        return methods.Contains(SyncDeviceCard.MethodFolder) ? "the shared folder" : "your relay";
    }

    private string ClockText(DateTimeOffset at)
    {
        return TimeZoneInfo.ConvertTime(at, Zone()).DateTime.ToString("h:mm tt", CultureInfo.InvariantCulture);
    }

    // ---- helpers ------------------------------------------------------------------------------------------------------------------------

    private List<Guid> AndroidIds()
    {
        return Active().Where(device => device.File.Device.Kind == SyncDeviceCard.KindPhone).Select(device => device.File.Device.DeviceId).Distinct().ToList();
    }

    // The roles the readers use. Ids are the applied ones plus the devices active now (a device that joined shows as Other or as having no role).
    private IReadOnlyDictionary<Guid, SyncDeviceRoleKind> ReaderMap(IReadOnlyList<ReceivedDevice> devices)
    {
        IEnumerable<Guid> ids = _document.AppliedIds
            .Concat(devices.Where(device => device.File.Device.Kind == SyncDeviceCard.KindPhone).Select(device => device.File.Device.DeviceId))
            .Distinct();
        // Only the entries that were applied FOR the devices they were applied for: a role entry for a device this PC did not know yet (its own file
        // is still to come) is no applied role, so it can't move the phones at once, in a session, or without the wait. A device that joined later
        // is Other when an applied main exists, else it has no role.
        IEnumerable<SyncDeviceRole> appliedEntries = _document.Applied.Where(entry => _document.AppliedIds.Contains(entry.Id));
        return SyncDeviceRoles.Effective(appliedEntries, ids);
    }

    private static bool LabelsDiffer(IReadOnlyDictionary<Guid, SyncDeviceRoleKind> before, IReadOnlyDictionary<Guid, SyncDeviceRoleKind> after)
    {
        return before.Any(pair => after.TryGetValue(pair.Key, out SyncDeviceRoleKind kind) && kind != pair.Value);
    }

    // What a change comes to, over the devices this PC applied: their roles and which of them are the phones. A device that joins meanwhile
    // does not make it another change.
    private static string TargetOf(IReadOnlyDictionary<Guid, SyncDeviceRoleKind> before, IReadOnlyDictionary<Guid, SyncDeviceRoleKind> after)
    {
        IEnumerable<string> roles = after.Where(pair => before.ContainsKey(pair.Key)).OrderBy(pair => pair.Key.ToString("D"), StringComparer.Ordinal)
            .Select(pair => $"{pair.Key:D}:{pair.Value}");
        IEnumerable<string> phones = SyncDeviceRoles.ThePhones(after).Where(before.ContainsKey).OrderBy(id => id.ToString("D"), StringComparer.Ordinal)
            .Select(id => id.ToString("D"));
        return string.Join(",", roles) + "|" + string.Join(",", phones);
    }

    // The list still holds the entry this PC wrote for its request.
    private bool OwnRequestAlive(DeviceRolesPending? pending)
    {
        return pending?.RequestedAt is { } at && _document.Held.Any(entry => entry.By == SelfId() && entry.At == at);
    }

    private HashSet<Guid> InvolvedIds()
    {
        return InvolvedIds(SyncDeviceRoles.Effective(_document.Applied, _document.AppliedIds), SyncDeviceRoles.Effective(_document.Held, AndroidIds())).ToHashSet();
    }

    // The devices a change moves: those whose role differs, and those that enter or leave "the phones".
    private static IEnumerable<Guid> InvolvedIds(IReadOnlyDictionary<Guid, SyncDeviceRoleKind> before, IReadOnlyDictionary<Guid, SyncDeviceRoleKind> after)
    {
        IReadOnlySet<Guid> was = SyncDeviceRoles.ThePhones(before);
        IReadOnlySet<Guid> now = SyncDeviceRoles.ThePhones(after);
        IEnumerable<Guid> changed = before.Where(pair => after.TryGetValue(pair.Key, out SyncDeviceRoleKind kind) && kind != pair.Value).Select(pair => pair.Key);
        return changed.Concat(was.Except(now)).Concat(now.Except(was).Where(before.ContainsKey)).Distinct();
    }

    private static bool SameEntries(IReadOnlyList<SyncDeviceRole> a, IReadOnlyList<SyncDeviceRole> b) => a.SequenceEqual(b);

    private void SetHeld(List<SyncDeviceRole> held)
    {
        if (SameEntries(held, _document.Held))
        {
            return;
        }

        _document = _document with { Held = held };
        Save();
        HeldChanged?.Invoke(this, EventArgs.Empty);
    }

    // Raises Changed when what the readers use or a row says is not what it was.
    private void Raise()
    {
        string fingerprint = string.Join(
            ";", _document.Applied.Select(entry => $"{entry.Id:D}{entry.Role}{entry.At.UtcTicks}"))
            + "|" + string.Join(",", _document.AppliedIds.Select(id => id.ToString("D")))
            + "|" + (_document.Pending is { } pending ? PendingText(pending) + pending.Target : string.Empty)
            + "|" + _lastAppliedAt?.UtcTicks;
        if (fingerprint == _lastFingerprint)
        {
            return;
        }

        _lastFingerprint = fingerprint;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Save()
    {
        DeviceRolesDocument snapshot = _document;
        // Saves go one after another, so a quick second change never lands on disk before the first.
        Task previous = _lastSave;
        _lastSave = SaveAfterAsync(previous, snapshot);
    }

    private async Task SaveAfterAsync(Task previous, DeviceRolesDocument snapshot)
    {
        try
        {
            await previous;
        }
        catch (SqliteException)
        {
            // The earlier write failed; this newer state is written anyway.
        }

        try
        {
            await _store.SaveAsync(DocumentKey, snapshot);
        }
        catch (SqliteException)
        {
            // Kept in memory; the next change writes it again.
        }
    }
}
