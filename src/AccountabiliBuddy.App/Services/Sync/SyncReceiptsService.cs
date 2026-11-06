using System.Globalization;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// Sync receipts, a developer switch (device-local document "dev.syncReceipts", off by default): a quiet notice when another
/// device's file shows it now holds a newer file of this PC's. Read from the device card's existing <c>seen</c> map, so it works the
/// same for the relay, Google Drive, a folder and QR codes, and adds nothing to any file. A watch writes no file, so it never confirms.
/// A device seen for the first time since the app started is noted without a notice. Use from the UI thread.
/// </summary>
public sealed class SyncReceiptsService
{
    private const string DocumentKey = "dev.syncReceipts";

    private readonly ReceivedDevicesService _received;
    private readonly SyncIdentityService _identity;
    private readonly SqliteJsonDocumentStore _store;
    private readonly TimeProvider _time;
    private readonly Action<string, string> _notify;
    private readonly Dictionary<Guid, DateTimeOffset> _confirmed = [];

    public SyncReceiptsService(
        ReceivedDevicesService received, SyncIdentityService identity, SqliteJsonDocumentStore store, TimeProvider time, Action<string, string> notify)
    {
        _received = received;
        _identity = identity;
        _store = store;
        _time = time;
        _notify = notify;
    }

    public bool IsOn { get; private set; }

    public void LoadBlocking()
    {
        try
        {
            IsOn = Task.Run(() => _store.LoadAsync<bool?>(DocumentKey)).GetAwaiter().GetResult() ?? false;
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            IsOn = false;
        }

        Check(notify: false);
    }

    public async Task SetOnAsync(bool on)
    {
        IsOn = on;
        try
        {
            await _store.SaveAsync(DocumentKey, on);
        }
        catch (SqliteException)
        {
            // Best effort; the switch still applies until the app closes.
        }
    }

    /// <summary>Looks at the received files again (call when they changed); notices only while on.</summary>
    public void Check(bool notify = true)
    {
        Guid me = _identity.DeviceId;
        if (me == Guid.Empty)
        {
            return;
        }

        List<ReceivedDevice> devices = _received.Active.Where(device => device.File.Device.Kind != SyncDeviceCard.KindWatch).ToList();
        foreach (ReceivedDevice device in devices)
        {
            Guid id = device.File.Device.DeviceId;
            if (!device.File.Device.Seen.TryGetValue(me, out DateTimeOffset seen))
            {
                continue;
            }

            bool known = _confirmed.TryGetValue(id, out DateTimeOffset before);
            _confirmed[id] = seen;
            if (!notify || !IsOn || !known || seen <= before)
            {
                continue;
            }

            IEnumerable<string> notYet = devices
                .Where(other => other.File.Device.DeviceId != id
                    && (!other.File.Device.Seen.TryGetValue(me, out DateTimeOffset otherSeen) || otherSeen < seen))
                .Select(other => other.File.Device.Name);
            string rest = string.Join(", ", notYet);
            _notify(
                $"Sync reached {device.File.Device.Name}",
                $"It has this PC's file from {Clock(seen)} (came back by {device.Method} at {Clock(device.ReceivedAt)})."
                    + (rest.Length > 0 ? $" Not yet: {rest}." : " Every device has it."));
        }
    }

    private string Clock(DateTimeOffset time) =>
        TimeZoneInfo.ConvertTime(time, _time.LocalTimeZone).ToString("h:mm tt", CultureInfo.InvariantCulture);
}
