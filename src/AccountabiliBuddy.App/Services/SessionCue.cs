namespace AccountabiliBuddy.App.Services;

/// <summary>A moment when time is up.</summary>
public enum SessionCue
{
    /// <summary>Focus is up: a break starts, or the session ends after focus.</summary>
    FocusEnded,

    /// <summary>A break is up: focus starts again, or the session ends after the break.</summary>
    BreakEnded,

    /// <summary>Planning time is up: the "Ready?" card asks (the focus-end chime plays, the overlay glows). Not an end of anything.</summary>
    PlanningEnded,
}
