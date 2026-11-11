namespace AccountabiliBuddy.Core.Ports;

/// <summary>
/// The default sound output's master volume and mute (ADR 4: Windows Core Audio lives behind this). Used only to let a ringing task
/// reminder through a muted or quiet PC ("Ring even when the PC is muted"), always followed by a restore of what it changed.
/// </summary>
public interface IMasterVolume
{
    /// <returns>The default output's state now; null when it can't be read (no output, remote session, refused).</returns>
    MasterVolumeState? Read();

    /// <returns>The state of the output <paramref name="deviceId"/> names, default or not; null when it is gone or can't be read.</returns>
    MasterVolumeState? Read(string deviceId);

    /// <summary>Sets the level (0 to 1) and mute of the output <paramref name="deviceId"/> names.</summary>
    /// <returns>False when the output is gone or refused the change.</returns>
    bool Apply(string deviceId, float level, bool muted);
}
