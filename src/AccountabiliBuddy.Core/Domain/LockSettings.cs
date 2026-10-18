namespace AccountabiliBuddy.Core.Domain;

/// <summary>How unlocks end.</summary>
/// <param name="RelockAtMidnight">Unlocks relock at the next midnight unless another time is picked when unlocking.</param>
public sealed record LockSettings(bool RelockAtMidnight)
{
    public static LockSettings Default { get; } = new(RelockAtMidnight: true);
}
