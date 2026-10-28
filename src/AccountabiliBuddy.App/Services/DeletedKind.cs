namespace AccountabiliBuddy.App.Services;

/// <summary>What a Recently deleted entry was.</summary>
public enum DeletedKind
{
    /// <summary>A task of the draft or the running checklist.</summary>
    Task,

    /// <summary>A step of such a task.</summary>
    Step,

    /// <summary>A task of an ended session's record (the Archive, Unfinished from past sessions).</summary>
    HistoryTask,

    /// <summary>A step of such a task.</summary>
    HistoryStep,

    /// <summary>An item of the Later list.</summary>
    Later,

    /// <summary>A come-back.</summary>
    ComeBack,
}
