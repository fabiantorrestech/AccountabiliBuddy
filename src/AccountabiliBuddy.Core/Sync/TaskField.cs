namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// The parts of a task that merge on their own (ADR 76): each has its own stamp in <see cref="TaskFieldStamps"/>, so a change
/// to one part on one device and to another part on another device both survive. Only <see cref="Title"/> and
/// <see cref="Note"/> can end in "Changed on both devices".
/// </summary>
public enum TaskField
{
    Title,
    Note,
    Steps,
    Label,
    Tags,

    /// <summary>The bin with its previous bin, position and done time (a move, a reorder, a tick).</summary>
    Bin,
    Reminders,
    Estimate,

    /// <summary>The due date and time.</summary>
    Due,
    Highlight,
    CantMiss,
    Devices,
    Calendar,
    Number,

    /// <summary>The day part it was put in the plan for (ADR 50).</summary>
    Plan,
}
