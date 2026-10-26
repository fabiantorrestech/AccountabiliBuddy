using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Ports;

/// <summary>
/// Tells when headphones connect or disconnect: wired ones (a headphone or headset output appearing) and paired
/// Bluetooth audio devices. Used to start the noise player on chosen focus headphones, and to keep it to headphones
/// (the player's lock and "Stop when headphones disconnect").
/// </summary>
public interface IHeadphoneWatcher : IDisposable
{
    /// <summary>Raised on a background thread. Outputs already connected at <see cref="Start"/> aren't reported.</summary>
    event EventHandler<HeadphoneEvent>? Changed;

    /// <summary>Raised on a background thread when Windows' default sound output changes (after <see cref="Start"/>).</summary>
    event EventHandler? DefaultOutputChanged;

    /// <summary>Starts watching (once; later calls do nothing).</summary>
    void Start();

    /// <summary>
    /// Whether any headphones are connected now: a wired headphone or headset output, or the output of any paired
    /// Bluetooth audio device (not only those chosen for auto-play). Starts watching if it hasn't, and waits a moment
    /// for the first look at the outputs.
    /// </summary>
    Task<bool> AnyConnectedAsync();

    /// <summary>
    /// Whether the default sound output (where sound goes now) is headphones, by the same test as
    /// <see cref="AnyConnectedAsync"/>; false for speakers, no output or one not seen yet. Starts watching if it hasn't.
    /// </summary>
    Task<bool> IsDefaultOutputHeadphonesAsync();

    /// <summary>The paired Bluetooth audio devices, to choose from in Settings.</summary>
    Task<IReadOnlyList<BluetoothAudioDevice>> ListBluetoothAudioAsync();
}
