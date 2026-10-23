using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Start the noise player when focus headphones connect, pause it when they go (app-owned document "headphones").
/// </summary>
/// <param name="Wired">Any wired headphones or headset. Off by default.</param>
/// <param name="Bluetooth">The chosen Bluetooth devices. Off by default.</param>
/// <param name="Devices">The Bluetooth devices chosen; with none, Bluetooth does nothing.</param>
public sealed record HeadphoneSettings(bool Wired, bool Bluetooth, IReadOnlyList<BluetoothAudioDevice> Devices)
{
    public static HeadphoneSettings Default { get; } = new(false, false, []);

    public bool IsOn => Wired || (Bluetooth && Devices.Count > 0);
}
