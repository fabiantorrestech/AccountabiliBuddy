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
}
