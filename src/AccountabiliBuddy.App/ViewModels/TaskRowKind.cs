namespace AccountabiliBuddy.App.ViewModels;

/// <summary>Which list a row of the Tasks page belongs to.</summary>
public enum TaskRowKind
{
    /// <summary>A task of this session: the next session's list, or the running checklist.</summary>
    Session,

    /// <summary>A come-back, a parked thought.</summary>
    ComeBack,

    /// <summary>An item of the Later list (ADR 50).</summary>
    Later,

    /// <summary>A task ticked today (running checklist or an ended session of today).</summary>
    Done,

    /// <summary>A task an ended session left open ("Unfinished from past sessions", the Archive).</summary>
    Past,

    /// <summary>A shutdown card's tomorrow task, kept when the card closed by itself; a plain row with a ⋯ menu only.</summary>
    AutoSaved,

    /// <summary>
    /// A task in Today's Queued while a session runs: it is in the task list only (the checklist holds the session's tasks), so its
    /// row works through the task list's actions (tick, rename, label, move, delete). While no session runs a Queued task is a
    /// <see cref="Session"/> row, since the next session's list holds it.
    /// </summary>
    Queued,
}
