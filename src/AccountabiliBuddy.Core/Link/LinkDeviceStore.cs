using System.Security.Cryptography.X509Certificates;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;

namespace AccountabiliBuddy.Core.Link;

/// <summary>
/// What the link keeps on this PC (documents table): "devices.settings" (on/off, port), "devices.paired" (each paired
/// device with a hash of its token) and "devices.identity" (the TLS certificate, encrypted with
/// <see cref="IDataProtector"/>). Safe to use from several threads.
/// </summary>
public sealed class LinkDeviceStore
{
    private const string SettingsKey = "devices.settings";
    private const string DevicesKey = "devices.paired";
    private const string IdentityKey = "devices.identity";

    // "Last seen" is saved at most this often; it's shown, not relied on.
    private static readonly TimeSpan LastSeenSaveInterval = TimeSpan.FromMinutes(5);

    private readonly SqliteJsonDocumentStore _store;
    private readonly IDataProtector _protector;
    private readonly object _lock = new();
    private List<PairedDevice> _devices = new();

    public LinkDeviceStore(SqliteJsonDocumentStore store, IDataProtector protector)
    {
        _store = store;
        _protector = protector;
    }

    public LinkSettings Settings { get; private set; } = LinkSettings.Default;

    public IReadOnlyList<PairedDevice> Devices
    {
        get
        {
            lock (_lock)
            {
                return _devices.ToList();
            }
        }
    }

    /// <summary>Raised when a device was paired, removed or seen.</summary>
    public event EventHandler? Changed;

    public async Task LoadAsync()
    {
        Settings = await _store.LoadAsync<LinkSettings>(SettingsKey).ConfigureAwait(false) ?? LinkSettings.Default;
        List<PairedDevice> devices = await _store.LoadAsync<List<PairedDevice>>(DevicesKey).ConfigureAwait(false) ?? new List<PairedDevice>();
        lock (_lock)
        {
            _devices = devices;
        }
    }

    public async Task SaveSettingsAsync(LinkSettings settings)
    {
        Settings = settings;
        await _store.SaveAsync(SettingsKey, settings).ConfigureAwait(false);
    }

    /// <summary>The PC's certificate, made the first time.</summary>
    public async Task<X509Certificate2> LoadOrCreateIdentityAsync(string pcName)
    {
        string? saved = await _store.LoadAsync<string>(IdentityKey).ConfigureAwait(false);
        if (saved is not null && _protector.Unprotect(Convert.FromBase64String(saved)) is string pfxBase64)
        {
            return LinkIdentity.Load(Convert.FromBase64String(pfxBase64));
        }

        // None yet, or made by another user or computer (it can't be decrypted here): a new one; devices pair again.
        byte[] pfx = LinkIdentity.CreatePfx(pcName);
        await _store.SaveAsync(IdentityKey, Convert.ToBase64String(_protector.Protect(Convert.ToBase64String(pfx)))).ConfigureAwait(false);
        return LinkIdentity.Load(pfx);
    }

    public async Task<PairedDevice> AddAsync(string name, string kind, string token, DateTimeOffset now)
    {
        var device = new PairedDevice(Guid.NewGuid(), name, kind, LinkSecrets.Hash(token), now, now);
        lock (_lock)
        {
            _devices = _devices.Append(device).ToList();
        }

        await SaveDevicesAsync().ConfigureAwait(false);
        return device;
    }

    /// <returns>The device when the id and token match a paired one; otherwise null.</returns>
    public PairedDevice? Authenticate(Guid deviceId, string token)
    {
        PairedDevice? device = Devices.FirstOrDefault(candidate => candidate.Id == deviceId);
        return device is not null && LinkSecrets.Matches(token, device.TokenHash) ? device : null;
    }

    public async Task MarkSeenAsync(Guid deviceId, DateTimeOffset now)
    {
        bool save;
        lock (_lock)
        {
            PairedDevice? device = _devices.FirstOrDefault(candidate => candidate.Id == deviceId);
            if (device is null)
            {
                return;
            }

            save = device.LastSeenAt is not DateTimeOffset seen || now - seen >= LastSeenSaveInterval;
            _devices = _devices.Select(candidate => candidate.Id == deviceId ? candidate with { LastSeenAt = now } : candidate).ToList();
        }

        if (save)
        {
            await SaveDevicesAsync().ConfigureAwait(false);
        }
        else
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task RemoveAsync(Guid deviceId)
    {
        lock (_lock)
        {
            _devices = _devices.Where(candidate => candidate.Id != deviceId).ToList();
        }

        await SaveDevicesAsync().ConfigureAwait(false);
    }

    private async Task SaveDevicesAsync()
    {
        await _store.SaveAsync(DevicesKey, Devices).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
