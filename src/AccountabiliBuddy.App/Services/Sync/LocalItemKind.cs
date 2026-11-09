namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>Which list an item made local (ADR 72) is in.</summary>
public enum LocalItemKind
{
    /// <summary>The next session's list (never the running checklist).</summary>
    NextTask,
    Later,
    ComeBack,
    AutoSaved,
    /// <summary>A task a past session left open or finished: this PC's history, another PC's, or one kept here.</summary>
    PastTask,
}
