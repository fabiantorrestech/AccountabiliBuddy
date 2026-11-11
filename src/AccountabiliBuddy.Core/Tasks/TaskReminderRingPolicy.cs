namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// How a task reminder rings (ADR 76, spec 05 §7), pure: its volume over time (rising over 30 s), when Dismiss becomes available
/// (dismiss protection; Can't miss also needs a hold), when it stops by itself and how often the sound repeats. Only Dismiss waits:
/// Snooze and Start always work at once. Can't miss rings at full volume, repeats faster and stops later.
/// </summary>
/// <param name="Volume">The reminder volume, 10 to 100 (default 70).</param>
/// <param name="StopAfterMinutes">Stops ringing after this long: 1, 5, 10 or 30.</param>
public sealed record TaskReminderRingPolicy(
    int Volume = 70,
    bool RisingVolume = true,
    bool DismissProtect = true,
    int DismissProtectSeconds = 5,
    int StopAfterMinutes = 10)
{
    public const int RisingSeconds = 30;
    public const int MinVolume = 10;
    public const int MaxStopAfterMinutes = 30;

    /// <summary>How long Can't miss must be held to dismiss.</summary>
    public static readonly TimeSpan HoldToDismiss = TimeSpan.FromSeconds(2);

    public static readonly IReadOnlyList<int> ProtectChoices = [3, 5, 10];
    public static readonly IReadOnlyList<int> StopAfterChoices = [1, 5, 10, 30];

    /// <summary>The sound volume (0 to 100) <paramref name="elapsed"/> after the ring started: rising from a quarter of the target to it over 30 s.</summary>
    public int VolumeAt(TimeSpan elapsed, bool cantMiss)
    {
        int target = cantMiss ? 100 : Math.Clamp(Volume, MinVolume, 100);
        if (!RisingVolume || cantMiss)
        {
            return target;
        }

        double progress = Math.Clamp(elapsed.TotalSeconds / RisingSeconds, 0, 1);
        double start = Math.Max(MinVolume, target / 4.0);
        return (int)Math.Round(start + ((target - start) * progress));
    }

    /// <summary>How long after it shows before Dismiss works (zero when protection is off). Can't miss adds a hold on top, see <see cref="HoldRequired"/>.</summary>
    public TimeSpan DismissWait()
    {
        return DismissProtect ? TimeSpan.FromSeconds(Nearest(ProtectChoices, DismissProtectSeconds)) : TimeSpan.Zero;
    }

    /// <summary>Hold-to-dismiss for Can't miss; none for a normal reminder.</summary>
    public TimeSpan HoldRequired(bool cantMiss)
    {
        return cantMiss ? HoldToDismiss : TimeSpan.Zero;
    }

    /// <summary>When it stops ringing by itself; Can't miss rings the longest.</summary>
    public TimeSpan StopAfter(bool cantMiss)
    {
        return TimeSpan.FromMinutes(cantMiss ? MaxStopAfterMinutes : Nearest(StopAfterChoices, StopAfterMinutes));
    }

    public TimeSpan RepeatEvery(bool cantMiss)
    {
        return TimeSpan.FromSeconds(cantMiss ? 5 : 10);
    }

    private static int Nearest(IReadOnlyList<int> choices, int value)
    {
        return choices.OrderBy(choice => Math.Abs(choice - value)).First();
    }
}
