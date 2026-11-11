using AccountabiliBuddy.Core.Ports;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// The decisions of "Ring even when the PC is muted" (spec: Implementation Notes), pure: what to raise the output to while a reminder
/// rings, and whether to put the user's own level back afterwards. It never lowers the volume and never touches an output the user
/// changed meanwhile: a level or mute they set during the ring stays.
/// </summary>
public static class MasterVolumePlan
{
    /// <summary>How far a level may differ from the one set before it counts as the user's own change.</summary>
    public const float Tolerance = 0.02f;

    /// <returns>The state to set while ringing at <paramref name="ringVolume"/> (0 to 100); null when the output already is loud enough and unmuted.</returns>
    public static MasterVolumeState? Raise(MasterVolumeState current, int ringVolume)
    {
        float wanted = Math.Clamp(ringVolume, 0, 100) / 100f;
        if (!current.Muted && current.Level >= wanted)
        {
            return null;
        }

        return current with { Level = Math.Max(current.Level, wanted), Muted = false };
    }

    /// <summary>True when the output still is as the raise left it, so the saved state should be put back; false when the user changed it or the output changed.</summary>
    public static bool ShouldRestore(MasterVolumeState raised, MasterVolumeState? now)
    {
        return now is not null
            && now.DeviceId == raised.DeviceId
            && now.Muted == raised.Muted
            && Math.Abs(now.Level - raised.Level) <= Tolerance;
    }
}
