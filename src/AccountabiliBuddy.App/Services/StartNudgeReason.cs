namespace AccountabiliBuddy.App.Services;

/// <summary>Why the start nudge came: some open Today in front, others only flash the taskbar button.</summary>
public enum StartNudgeReason
{
    /// <summary>The user arrived during a work part: at its start while at the computer, or the first input after it started while away.</summary>
    WorkStarted,

    /// <summary>The first check after launch, during a work part, with the user at the computer.</summary>
    AppStarted,

    /// <summary>Back at the desk after <see cref="StartNudgeService.AwayFor"/> with no input.</summary>
    Back,

    /// <summary>A session just ended inside a work part.</summary>
    SessionEnded,

    /// <summary>A calendar event, lunch or the met goal stopped holding the nudge back inside a work part.</summary>
    Resumed,

    /// <summary>Start on the start-nudge card.</summary>
    CardStart,
}
