using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Settings → Noise player → "Play when headphones connect": wired on/off, Bluetooth on/off and which paired devices.
/// Changes apply at once.
/// </summary>
public sealed partial class HeadphoneSettingsViewModel : ObservableObject
{
    private readonly HeadphoneAutoPlayService _service;
    private bool _loading;

    public HeadphoneSettingsViewModel(HeadphoneAutoPlayService service)
    {
        _service = service;
        _loading = true;
        Wired = service.Current.Wired;
        Bluetooth = service.Current.Bluetooth;
        // Each choice reports a change as it's built, before it's in Devices; saving then would drop it.
        ShowChosen(service.Current.Devices);
        _loading = false;
        if (Bluetooth)
        {
            _ = RefreshDevicesAsync();
        }
    }

    [ObservableProperty]
    public partial bool Wired { get; set; }

    [ObservableProperty]
    public partial bool Bluetooth { get; set; }

    public ObservableCollection<HeadphoneChoiceViewModel> Devices { get; } = new();

    /// <summary>E.g. "Pick at least one device" or "No paired Bluetooth audio devices".</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    partial void OnWiredChanged(bool value) => _ = SaveAsync();

    partial void OnBluetoothChanged(bool value)
    {
        _ = SaveAsync();
        if (value)
        {
            _ = RefreshDevicesAsync();
        }
    }

    /// <summary>Reads the paired Bluetooth audio devices again (chosen ones that are no longer paired stay listed).</summary>
    [RelayCommand]
    private async Task RefreshDevicesAsync()
    {
        IReadOnlyList<BluetoothAudioDevice> paired = await _service.ListBluetoothAsync();
        IReadOnlyList<BluetoothAudioDevice> chosen = _service.Current.Devices;
        List<BluetoothAudioDevice> all = [.. paired, .. chosen.Where(device => paired.All(other => other.Id != device.Id))];
        _loading = true;
        Devices.Clear();
        foreach (BluetoothAudioDevice device in all)
        {
            Devices.Add(new HeadphoneChoiceViewModel(device, chosen.Any(other => other.Id == device.Id), () => _ = SaveAsync()));
        }

        _loading = false;
        ShowStatus();
    }

    private void ShowChosen(IReadOnlyList<BluetoothAudioDevice> chosen)
    {
        foreach (BluetoothAudioDevice device in chosen)
        {
            Devices.Add(new HeadphoneChoiceViewModel(device, true, () => _ = SaveAsync()));
        }

        ShowStatus();
    }

    private void ShowStatus()
    {
        Status = !Bluetooth ? string.Empty
            : Devices.Count == 0 ? "No paired Bluetooth audio devices. Pair them in Windows' Bluetooth settings, then press Look again."
            : Devices.Any(device => device.IsChosen) ? string.Empty
            : "Tick the headphones that should start the noise.";
    }

    private async Task SaveAsync()
    {
        if (_loading)
        {
            return;
        }

        ShowStatus();
        List<BluetoothAudioDevice> chosen = Devices.Where(device => device.IsChosen).Select(device => device.Device).ToList();
        await _service.SaveAsync(new HeadphoneSettings(Wired, Bluetooth, chosen));
    }
}
