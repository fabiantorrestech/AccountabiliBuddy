namespace AccountabiliBuddy.Core.Domain;

/// <summary>Why the user stepped away during a session. Either way the countdown pauses until they're back.</summary>
public enum AwayKind
{
    /// <summary>A short break: bathroom, a drink.</summary>
    Break,

    /// <summary>Someone or something needed the user: a colleague, a call.</summary>
    Interrupted,

    /// <summary>
    /// Marked distracted afterwards in History (<see cref="SessionGap"/>); never written as a session event, only
    /// folded into a record by <see cref="Engine.SessionGaps.Apply"/>.
    /// </summary>
    Distracted,
}
