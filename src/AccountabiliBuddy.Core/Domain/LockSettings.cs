namespace AccountabiliBuddy.Core.Domain;

/// <summary>How unlocks end, and how long looser changes wait.</summary>
/// <param name="RelockAtMidnight">Unlocks relock at the next midnight unless another time is picked when unlocking.</param>
public sealed record LockSettings(bool RelockAtMidnight)
{
    public static TimeSpan DefaultLooseningDelay { get; } = TimeSpan.FromMinutes(10);

    public static LockSettings Default { get; } = new(RelockAtMidnight: true);

    /// <summary>
    /// How long a looser change (e.g. deleting a scheduled block) waits before it takes effect (schema v3).
    /// Stricter changes take effect at once.
    /// </summary>
    public TimeSpan LooseningDelay { get; init; } = DefaultLooseningDelay;
}
