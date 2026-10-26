namespace AccountabiliBuddy.App.Services;

/// <summary>Why the start nudge came: some open Today in front, others only flash the taskbar button.</summary>
public enum StartNudgeReason
{
    /// <summary>A work part just started.</summary>
    WorkStarted,

    /// <summary>The first check after launch, during a work part.</summary>
    AppStarted,

    /// <summary>Back at the desk after <see cref="StartNudgeService.AwayFor"/> with no input.</summary>
    Back,

    /// <summary>A session just ended inside a work part.</summary>
    SessionEnded,

    /// <summary>Still no session, every few minutes.</summary>
    Repeat,
}
