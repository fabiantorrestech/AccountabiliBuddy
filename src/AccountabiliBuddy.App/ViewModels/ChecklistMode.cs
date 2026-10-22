namespace AccountabiliBuddy.App.ViewModels;

/// <summary>Where a guided session's checklist shows (ADR 24, 25).</summary>
public enum ChecklistMode
{
    /// <summary>Before Start (the quick-session popup, the Session tab): add, edit and remove; nothing to tick yet.</summary>
    Draft,

    /// <summary>The running session on the Session tab: tick, add, edit, remove.</summary>
    Live,

    /// <summary>The on-screen window's Ctrl+Alt details: tick only, small.</summary>
    Compact,
}
