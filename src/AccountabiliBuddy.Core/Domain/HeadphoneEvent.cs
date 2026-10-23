namespace AccountabiliBuddy.Core.Domain;

/// <summary>Headphones connected or disconnected.</summary>
/// <param name="Connected">True when they connected, false when they went away.</param>
/// <param name="IsBluetooth">A paired Bluetooth device; false for wired headphones or a headset.</param>
/// <param name="DeviceId">The Bluetooth device's id (as in <see cref="BluetoothAudioDevice.Id"/>); null for wired.</param>
/// <param name="Name">The output's name, e.g. "Headphones (WH-1000XM4)".</param>
public sealed record HeadphoneEvent(bool Connected, bool IsBluetooth, string? DeviceId, string Name);
