namespace AccountabiliBuddy.Core.Sync;

/// <summary>The <see cref="SyncActionResult.Status"/> values (ADR 42); every result has one.</summary>
public static class SyncActionStatus
{
    /// <summary>Applied, or nothing left to do (e.g. a task already ticked).</summary>
    public const string Applied = "applied";

    /// <summary>
    /// A task add that the PC parked as a come-back instead of adding (the checklist was full): <see cref="SyncActionResult.Ok"/> is true and
    /// the message says so. A phone that predates this status shows the message and takes it as applied, as before; a newer one keeps the
    /// details of what it sent (a Later item's note, steps, estimate and due date, which a parked come-back does not carry).
    /// </summary>
    public const string Parked = "parked";

    /// <summary>A day plan edit made from an older PC plan: the result carries the PC's version and edit time.</summary>
    public const string Conflict = "conflict";

    /// <summary>A day plan edit during a counted focus session: the phone keeps it and sends it again later with a new id.</summary>
    public const string Locked = "locked";

    /// <summary>The action couldn't be read, or the plan breaks the editor's rules.</summary>
    public const string Invalid = "invalid";

    /// <summary>Refused by the PC's rules (a session already runs, the wait isn't over, the reason too short…).</summary>
    public const string Refused = "refused";

    /// <summary>Arrived after its freshness ran out; never applied late.</summary>
    public const string Late = "late";

    /// <summary>A kind this PC doesn't know.</summary>
    public const string Unsupported = "unsupported";
}
