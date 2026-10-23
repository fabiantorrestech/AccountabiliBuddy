using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Ports;

/// <summary>
/// Tells when headphones connect or disconnect: wired ones (a headphone or headset output appearing) and paired
/// Bluetooth audio devices. Used to start the noise player on chosen focus headphones.
/// </summary>
public interface IHeadphoneWatcher : IDisposable
{
    /// <summary>Raised on a background thread. Outputs already connected at <see cref="Start"/> aren't reported.</summary>
    event EventHandler<HeadphoneEvent>? Changed;

    /// <summary>Starts watching (once; later calls do nothing).</summary>
    void Start();

    /// <summary>The paired Bluetooth audio devices, to choose from in Settings.</summary>
    Task<IReadOnlyList<BluetoothAudioDevice>> ListBluetoothAudioAsync();
}
