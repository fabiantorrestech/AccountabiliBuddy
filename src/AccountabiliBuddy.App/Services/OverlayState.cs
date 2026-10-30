namespace AccountabiliBuddy.App.Services;

/// <summary>What the overlay is saying right now (13a), most urgent first.</summary>
public enum OverlayState
{
    /// <summary>"Nothing running · Start": no session, and nothing else to say.</summary>
    Between,

    /// <summary>A focus session, with time left.</summary>
    Focus,

    /// <summary>The last 2 minutes of a focus phase: the ring and the time go ink.</summary>
    WrapUp,

    /// <summary>A break (Sleep colour).</summary>
    Break,

    /// <summary>The planning minutes before the focus countdown (muted).</summary>
    Planning,

    /// <summary>Stepped away or on a side task: the countdown is paused.</summary>
    Away,

    /// <summary>Drift step 1: a 1 px Drift border and a dot.</summary>
    Drift,

    /// <summary>Between sessions, a calendar event starts within 10 minutes.</summary>
    MeetingSoon,

    /// <summary>The day's planned work is done (Met).</summary>
    WorkDone,

    /// <summary>Bedtime is near or past (Sleep border).</summary>
    WindDown,
}
