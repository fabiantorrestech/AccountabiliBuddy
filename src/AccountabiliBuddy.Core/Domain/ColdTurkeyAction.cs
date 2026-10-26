namespace AccountabiliBuddy.Core.Domain;

/// <summary>One Cold Turkey command to run (ADR 37, 39), in order, and why.</summary>
public sealed record ColdTurkeyAction(string Kind, string Block, string Why)
{
    /// <summary>Start (if off) and lock for <see cref="Engine.ColdTurkeyHold.LockMinutes"/>; extends a running lock.</summary>
    public const string LockTimed = "lockTimed";

    /// <summary>Start and lock with the app's password.</summary>
    public const string LockPassword = "lockPassword";

    /// <summary>Stop with the app's password (a plain start follows when the block should stay on).</summary>
    public const string StopPassword = "stopPassword";

    /// <summary>Start, unlocked.</summary>
    public const string Start = "start";

    /// <summary>Stop an unlocked block.</summary>
    public const string Stop = "stop";

    /// <summary>A password-held block went off in its window: the user's spare key; release it for today, new key.</summary>
    public const string SpareKeyUsed = "spareKeyUsed";
}
