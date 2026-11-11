namespace AccountabiliBuddy.Core.Tasks;

/// <summary>What bringing a step into focus did (ADR 76).</summary>
public enum SessionFocusResult
{
    /// <summary>The step is in focus now (or already was).</summary>
    Focused,

    /// <summary>This session is full: nothing changed; the caller offers the swap ("This session is full").</summary>
    Full,

    /// <summary>No such open step of an open task in the checklist.</summary>
    NotFound,
}
