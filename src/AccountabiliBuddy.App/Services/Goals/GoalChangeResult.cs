namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>What became of a goal change asked for through <see cref="GoalsService"/>.</summary>
public enum GoalChangeResult
{
    /// <summary>Nothing to change (the same value).</summary>
    Unchanged,

    /// <summary>Saved and in effect.</summary>
    Applied,

    /// <summary>A looser Deep work change kept as a waiting change (shown with Cancel); it applies once the rules allow.</summary>
    Waiting,

    /// <summary>Refused: a looser Deep work change during a session outside ProductaBuddy mode.</summary>
    RefusedDuringSession,

    /// <summary>Refused: Deep work can't be paused, deleted, logged by hand or given other sources; a "Coming soon" source; an unknown item.</summary>
    NotAllowed,

    /// <summary>Refused: the goals aren't ready on this PC (<see cref="GoalModelState"/> is not Ready).</summary>
    NotAvailable,
}
