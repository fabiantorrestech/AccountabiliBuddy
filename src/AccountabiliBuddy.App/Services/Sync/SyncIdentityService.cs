using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// Who this PC is in the user's device group (ADR 22): its device id (made once, kept for good), its name, and the
/// group's link code, from which the sync key comes, and its ECDH key pair for re-keying (ADR 54). The code and the key
/// pair's private half are stored only DPAPI-protected (<see cref="IDataProtector"/>) in the documents table
/// ("sync.identity"). Use from the UI thread only.
/// </summary>
public sealed class SyncIdentityService
{
    private const string DocumentKey = "sync.identity";

    private readonly SqliteJsonDocumentStore _store;
    private readonly IDataProtector _protector;
    private readonly ReceivedDevicesService _received;

    private string? _code;
    private SyncDeviceKey? _deviceKey;

    public SyncIdentityService(SqliteJsonDocumentStore store, IDataProtector protector, ReceivedDevicesService received)
    {
        _store = store;
        _protector = protector;
        _received = received;
    }

    public Guid DeviceId { get; private set; }

    /// <summary>The name other devices show for this PC.</summary>
    public string DeviceName => Environment.MachineName;

    /// <summary>The group's key; null while this PC isn't in a group.</summary>
    public SyncKey? Key { get; private set; }

    /// <summary>This PC's key pair for receiving a new link code when the group is re-keyed (ADR 54); made once, kept for good.</summary>
    public SyncDeviceKey DeviceKey => _deviceKey ??= SyncDeviceKey.Generate();

    /// <summary>Raised when this PC creates, joins or leaves a group.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised, just before <see cref="Changed"/>, when <see cref="SwitchKeyAsync"/> replaced the key.</summary>
    public event EventHandler<SyncKeySwitchedEventArgs>? KeySwitched;

    /// <summary>Loads the identity; on the first run makes the device id and saves it.</summary>
    public void LoadBlocking()
    {
        StoredIdentity? stored = null;
        try
        {
            stored = Task.Run(() => _store.LoadAsync<StoredIdentity>(DocumentKey)).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            stored = null;
        }

        if (stored is null || stored.DeviceId == Guid.Empty)
        {
            DeviceId = Guid.NewGuid();
            SetCode(null);
            Task.Run(() => SaveAsync()).GetAwaiter().GetResult();
            return;
        }

        DeviceId = stored.DeviceId;
        // A code that can't be unprotected (copied from another user or PC) means this PC isn't in the group.
        SetCode(UnprotectCode(stored.ProtectedCode));
        // A key pair that can't be unprotected is made again; the next file carries the new public key.
        if (UnprotectDeviceKey(stored.ProtectedDeviceKey) is SyncDeviceKey deviceKey)
        {
            _deviceKey = deviceKey;
        }
        else
        {
            Task.Run(() => SaveAsync()).GetAwaiter().GetResult();
        }
    }

    /// <returns>The link code in groups of 5 (e.g. "8KQ2V-7XHPM-…") to show or put in a QR code; null outside a group.</returns>
    public string? CodeForDisplay()
    {
        return _code is null ? null : SyncKeyCode.Display(_code);
    }

    /// <summary>Starts a new group with a new link code; this PC leaves the one it was in.</summary>
    public Task CreateGroupAsync()
    {
        return ChangeCodeAsync(SyncKeyCode.NewCode());
    }

    /// <returns>False when the code isn't a link code (nothing changes).</returns>
    public async Task<bool> JoinAsync(string code)
    {
        if (!SyncKeyCode.TryParse(code, out _))
        {
            return false;
        }

        await ChangeCodeAsync(SyncKeyCode.Display(code));
        return true;
    }

    /// <summary>Forgets the code, the key and every device received from the group.</summary>
    public Task LeaveAsync()
    {
        return ChangeCodeAsync(null);
    }

    /// <summary>
    /// Replaces the group's key in place (ADR 54): same group, new link code, after a device was removed. Unlike joining
    /// or leaving, it keeps every device received from the group. <see cref="KeySwitched"/> is raised first, so the sync
    /// methods can clear the old group's files, then <see cref="Changed"/>.
    /// </summary>
    /// <param name="removedDeviceIds">The devices the rekey removed; their old files are cleared too.</param>
    /// <returns>False (nothing changes) when this PC isn't in a group or the code isn't a link code.</returns>
    public async Task<bool> SwitchKeyAsync(string newCode, IReadOnlyList<Guid> removedDeviceIds)
    {
        if (Key is not SyncKey keyBefore || !SyncKeyCode.TryParse(newCode, out _))
        {
            return false;
        }

        SetCode(SyncKeyCode.Display(newCode));
        await SaveAsync();
        KeySwitched?.Invoke(this, new SyncKeySwitchedEventArgs(keyBefore, removedDeviceIds));
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Drops the code and the key but keeps what was received (this PC was removed, ADR 54): nothing syncs until it
    /// joins a group again. <see cref="LeaveAsync"/> also forgets the devices.
    /// </summary>
    public async Task DropKeyKeepingDataAsync()
    {
        SetCode(null);
        await SaveAsync();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task ChangeCodeAsync(string? code)
    {
        SyncKey? keyBefore = Key;
        SetCode(code);
        await SaveAsync();
        // Another group's devices (or none) are no longer this PC's to show.
        if (!(Key?.SameGroupAs(keyBefore) ?? keyBefore is null))
        {
            await _received.ForgetAllAsync();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SetCode(string? code)
    {
        if (code is not null && SyncKeyCode.TryParse(code, out SyncKey? key))
        {
            _code = SyncKeyCode.Display(code).Replace("-", "");
            Key = key;
        }
        else
        {
            _code = null;
            Key = null;
        }
    }

    private string? UnprotectCode(string? protectedCode)
    {
        if (protectedCode is null)
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(Convert.FromBase64String(protectedCode));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private SyncDeviceKey? UnprotectDeviceKey(string? protectedKey)
    {
        if (protectedKey is null)
        {
            return null;
        }

        try
        {
            string? pkcs8 = _protector.Unprotect(Convert.FromBase64String(protectedKey));
            if (pkcs8 is null)
            {
                return null;
            }

            return SyncDeviceKey.TryFromPkcs8(Convert.FromBase64String(pkcs8));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private Task SaveAsync()
    {
        string? protectedCode = _code is null ? null : Convert.ToBase64String(_protector.Protect(_code));
        string protectedKey = Convert.ToBase64String(_protector.Protect(Convert.ToBase64String(DeviceKey.ExportPkcs8())));
        return _store.SaveAsync(DocumentKey, new StoredIdentity(DeviceId, protectedCode, protectedKey));
    }

    /// <param name="ProtectedCode">The link code, protected, as base64; null outside a group.</param>
    /// <param name="ProtectedDeviceKey">The key pair's PKCS#8 private key (as base64), protected, as base64; null before ADR 54.</param>
    private sealed record StoredIdentity(Guid DeviceId, string? ProtectedCode, string? ProtectedDeviceKey = null);
}
