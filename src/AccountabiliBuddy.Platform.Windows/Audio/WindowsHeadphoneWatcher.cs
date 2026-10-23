using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Media.Devices;

namespace AccountabiliBuddy.Platform.Windows.Audio;

/// <summary>
/// Watches Windows' audio outputs. An output that turns on after the watch started is reported as connected: a
/// Bluetooth one when it belongs to a paired Bluetooth device (same container id), a wired one when it says it's
/// headphones or a headset. Whether plugging into a headphone jack makes a new output depends on the sound driver;
/// USB and Bluetooth headphones always do.
/// </summary>
public sealed class WindowsHeadphoneWatcher : IHeadphoneWatcher
{
    private const string ContainerIdKey = "System.Devices.ContainerId";
    private const string EnabledKey = "System.Devices.InterfaceEnabled";
    // PKEY_AudioEndpoint_FormFactor: 3 = headphones, 5 = headset.
    private const string FormFactorKey = "{1da5d803-d492-4edd-8c23-e0c0ffee7f0e} 0";
    private const string AepContainerKey = "System.Devices.Aep.ContainerId";
    private const string CodMajorKey = "System.Devices.Aep.Bluetooth.Cod.Major";
    private const uint HeadphonesFormFactor = 3;
    private const uint HeadsetFormFactor = 5;
    private const ushort AudioVideoMajorClass = 4;

    private readonly object _gate = new();
    private readonly Dictionary<string, Output> _outputs = new();
    private IReadOnlyList<PairedDevice> _paired = [];
    private DeviceWatcher? _watcher;
    private bool _enumerated;

    public event EventHandler<HeadphoneEvent>? Changed;

    public void Start()
    {
        if (_watcher is not null)
        {
            return;
        }

        _ = RefreshPairedAsync();
        _watcher = DeviceInformation.CreateWatcher(
            MediaDevice.GetAudioRenderSelector(), [ContainerIdKey, EnabledKey, FormFactorKey], DeviceInformationKind.DeviceInterface);
        _watcher.Added += (_, info) => OnAdded(info);
        _watcher.Updated += (_, update) => OnUpdated(update);
        _watcher.Removed += (_, update) => OnGone(update.Id);
        _watcher.EnumerationCompleted += (_, _) =>
        {
            lock (_gate)
            {
                _enumerated = true;
            }
        };
        _watcher.Start();
    }

    public async Task<IReadOnlyList<BluetoothAudioDevice>> ListBluetoothAudioAsync()
    {
        IReadOnlyList<PairedDevice> paired = await RefreshPairedAsync();
        return paired.Select(device => device.Device).ToList();
    }

    public void Dispose()
    {
        if (_watcher is { Status: DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted })
        {
            _watcher.Stop();
        }

        _watcher = null;
    }

    private void OnAdded(DeviceInformation info)
    {
        var output = new Output(info.Name, Read<Guid>(info.Properties, ContainerIdKey), Read<uint>(info.Properties, FormFactorKey), info.IsEnabled);
        bool report;
        lock (_gate)
        {
            _outputs[info.Id] = output;
            report = _enumerated && output.Enabled;
        }

        if (report)
        {
            _ = ReportAsync(output, connected: true);
        }
    }

    private void OnUpdated(DeviceInformationUpdate update)
    {
        if (!update.Properties.TryGetValue(EnabledKey, out object? value) || value is not bool enabled)
        {
            return;
        }

        Output? before;
        lock (_gate)
        {
            if (!_outputs.TryGetValue(update.Id, out before) || before.Enabled == enabled)
            {
                return;
            }

            _outputs[update.Id] = before with { Enabled = enabled };
        }

        _ = ReportAsync(before, connected: enabled);
    }

    private void OnGone(string id)
    {
        Output? before;
        lock (_gate)
        {
            if (!_outputs.Remove(id, out before) || !before.Enabled)
            {
                return;
            }
        }

        _ = ReportAsync(before, connected: false);
    }

    private async Task ReportAsync(Output output, bool connected)
    {
        try
        {
            PairedDevice? paired = Match(_paired, output);
            if (paired is null && output.Container is not null && connected)
            {
                // Paired since the last look: read the list again.
                paired = Match(await RefreshPairedAsync(), output);
            }

            if (paired is not null)
            {
                Changed?.Invoke(this, new HeadphoneEvent(connected, true, paired.Device.Id, output.Name));
            }
            else if (output.FormFactor is HeadphonesFormFactor or HeadsetFormFactor)
            {
                Changed?.Invoke(this, new HeadphoneEvent(connected, false, null, output.Name));
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            // Bluetooth off or unavailable: nothing to report.
        }
    }

    private static PairedDevice? Match(IReadOnlyList<PairedDevice> paired, Output output) =>
        output.Container is Guid container ? paired.FirstOrDefault(device => device.Container == container) : null;

    private async Task<IReadOnlyList<PairedDevice>> RefreshPairedAsync()
    {
        try
        {
            DeviceInformationCollection found = await DeviceInformation.FindAllAsync(
                BluetoothDevice.GetDeviceSelectorFromPairingState(true), [AepContainerKey, CodMajorKey], DeviceInformationKind.AssociationEndpoint);
            List<PairedDevice> paired = found
                .Where(info => Read<ushort>(info.Properties, CodMajorKey) is null or AudioVideoMajorClass)
                .Where(info => !string.IsNullOrWhiteSpace(info.Name))
                .Select(info => new PairedDevice(new BluetoothAudioDevice(info.Id, info.Name), Read<Guid>(info.Properties, AepContainerKey)))
                .OrderBy(device => device.Device.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            _paired = paired;
            return paired;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            return _paired;
        }
    }

    private static T? Read<T>(IReadOnlyDictionary<string, object> properties, string key)
        where T : struct =>
        properties.TryGetValue(key, out object? value) && value is T typed ? typed : null;

    private sealed record Output(string Name, Guid? Container, uint? FormFactor, bool Enabled);

    private sealed record PairedDevice(BluetoothAudioDevice Device, Guid? Container);
}
