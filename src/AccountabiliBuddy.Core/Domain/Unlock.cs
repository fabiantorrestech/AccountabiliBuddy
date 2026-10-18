namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// An unlock the user asked for: it waits until <see cref="ReadyAt"/>, then lifts the blocks until
/// <see cref="RelockAt"/> (or until the user locks again, when null).
/// </summary>
/// <param name="OptionName">The <see cref="UnlockOption"/> that was used, e.g. "Off day".</param>
/// <param name="Reason">What the user typed when asking (logged encrypted when the session ends).</param>
/// <param name="Activated">Set once the unlock took effect and ended the session that was running then,
/// so sessions started later during the unlock are left alone.</param>
public sealed record Unlock(
    string OptionName,
    DateTimeOffset RequestedAt,
    DateTimeOffset ReadyAt,
    DateTimeOffset? RelockAt,
    string Reason,
    bool Activated = false)
{
    public UnlockStatus StatusAt(DateTimeOffset now)
    {
        if (now < ReadyAt)
        {
            return UnlockStatus.Waiting;
        }

        return RelockAt is DateTimeOffset relock && now >= relock ? UnlockStatus.None : UnlockStatus.Unlocked;
    }
}
