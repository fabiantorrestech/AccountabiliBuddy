namespace AccountabiliBuddy.Core.Domain;

public enum SessionEventKind
{
    Started,

    /// <summary>Ran out on its own (timed manual session, Pomodoro with a cycle limit, scheduled block).</summary>
    Completed,

    /// <summary>Ended by the user during a focus phase, with the friction reason.</summary>
    EndedEarly,

    /// <summary>Ended by the user outside a focus phase (e.g. during a break), no friction.</summary>
    Ended,
}
