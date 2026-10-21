using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// The other devices' parts this PC has received (ADR 22), one merged copy per device, kept in the documents table
/// ("sync.received"). Received sessions are kept 90 days, like this PC's own history (ADR 8). Use from the UI thread
/// only; <see cref="Changed"/> is raised there.
/// </summary>
public sealed class ReceivedDevicesService
{
    private const string DocumentKey = "sync.received";

    private readonly SqliteJsonDocumentStore _store;
    private readonly TimeProvider _time;

    // Changes and their saves run one at a time, so an older list never overwrites a newer one.
    private readonly SemaphoreSlim _changing = new(1, 1);

    private List<ReceivedDevice> _devices = [];

    public ReceivedDevicesService(SqliteJsonDocumentStore store, TimeProvider time)
    {
        _store = store;
        _time = time;
    }

    public IReadOnlyList<ReceivedDevice> Devices => _devices;

    /// <summary>Why the last file couldn't be used, e.g. "Can't read a file from <folder>"; null when fine.</summary>
    public string? LastProblem { get; private set; }

    /// <summary>Raised when a device's copy is added, changes or is forgotten, and when <see cref="LastProblem"/> changes.</summary>
    public event EventHandler? Changed;

    public void LoadBlocking()
    {
        try
        {
            List<ReceivedDevice>? loaded = Task.Run(() => _store.LoadAsync<List<ReceivedDevice>>(DocumentKey)).GetAwaiter().GetResult();
            _devices = loaded?.Where(device => device?.File?.Device is not null && device.Sealed is not null).ToList() ?? [];
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            _devices = [];
        }
    }

    /// <summary>
    /// Merges a file another device sealed into this PC's copy of that device (<see cref="SyncMerge.Merge"/>). A file
    /// written before the copy changes nothing.
    /// </summary>
    /// <param name="method">How it came (see <see cref="ReceivedDevice.Method"/>).</param>
    /// <param name="sealedBytes">The file's sealed bytes as they arrived, kept for passing on.</param>
    public async Task AcceptAsync(SyncFile file, string method, byte[] sealedBytes)
    {
        await _changing.WaitAsync();
        try
        {
            ReceivedDevice? stored = _devices.FirstOrDefault(device => device.File.Device.DeviceId == file.Device.DeviceId);
            DateTimeOffset now = _time.GetUtcNow();
            SyncFile merged = SyncMerge.Merge(stored?.File, file, now);
            if (stored is not null && ReferenceEquals(merged, stored.File))
            {
                return;
            }

            var updated = _devices.Where(device => !ReferenceEquals(device, stored)).ToList();
            updated.Add(new ReceivedDevice(merged, now, method, sealedBytes));
            await SaveAsync(updated);
        }
        finally
        {
            _changing.Release();
        }
    }

    /// <summary>Drops this PC's copy of one device; a later file from it starts a new one.</summary>
    public async Task ForgetAsync(Guid deviceId)
    {
        await _changing.WaitAsync();
        try
        {
            await SaveAsync(_devices.Where(device => device.File.Device.DeviceId != deviceId).ToList());
        }
        finally
        {
            _changing.Release();
        }
    }

    /// <summary>Drops every received device, e.g. on leaving the device group.</summary>
    public async Task ForgetAllAsync()
    {
        await _changing.WaitAsync();
        try
        {
            LastProblem = null;
            await SaveAsync([]);
        }
        finally
        {
            _changing.Release();
        }
    }

    /// <summary>Shows why a file couldn't be used (null clears it). Says nothing about what's in the file.</summary>
    public void SetProblem(string? problem)
    {
        if (problem != LastProblem)
        {
            LastProblem = problem;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task SaveAsync(List<ReceivedDevice> devices)
    {
        _devices = devices;
        Changed?.Invoke(this, EventArgs.Empty);
        await _store.SaveAsync(DocumentKey, devices);
    }
}
