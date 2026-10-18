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

    /// <summary>The app stopped mid-session without ending it (crash, killed, restart); recorded at the last moment it was known to run.</summary>
    Interrupted,

    /// <summary>The app started again and carried on with the interrupted session.</summary>
    Resumed,
}
