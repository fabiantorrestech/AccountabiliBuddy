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

    /// <summary>The user stepped away (the countdown pauses); the reason holds the <see cref="AwayKind"/> name.</summary>
    AwayStarted,

    /// <summary>The user was back.</summary>
    AwayEnded,

    /// <summary>The user added 5 minutes to a side task; the reason they typed is stored encrypted and never leaves the PC.</summary>
    SideTaskExtended,

    /// <summary>The session's planning stage began (at the session's start); it is not focus time.</summary>
    PlanningStarted,

    /// <summary>The planning stage ended (Ready, a ticked task, Start on the card); focus began.</summary>
    PlanningEnded,
}
