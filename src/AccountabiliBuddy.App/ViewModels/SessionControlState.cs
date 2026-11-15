namespace AccountabiliBuddy.App.ViewModels;

/// <summary>What the title bar's session control shows (Tasks redesign `03` §A).</summary>
public enum SessionControlState
{
    /// <summary>No session: the "Start session" pill (its shortcut in the tooltip).</summary>
    Idle,

    /// <summary>A session runs (focus, planning, a break, or the timer paused): the ring and the time left.</summary>
    Running,

    /// <summary>Stepped away or on a side task: "Away 6 min" and "I'm back".</summary>
    Away,

    /// <summary>A real session ended and Tasks still shows its layout: "Ended · switches next time".</summary>
    Ended,

    /// <summary>
    /// No session here, but one is live on another device of the group (ADR 85, R1 and R2): "Session on Pixel · 32 min left", not
    /// clickable (only that device can end it, R6). Start is held back until it ends or goes quiet (R3).
    /// </summary>
    Elsewhere,
}
