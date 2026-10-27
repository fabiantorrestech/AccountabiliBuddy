namespace AccountabiliBuddy.Core.Engine;

/// <summary>How the Setup DP Assistant describes a workday's work time.</summary>
public enum DpWorkTime
{
    /// <summary>A work window ("9:00 AM to 5:00 PM"): how long the user is at work, or the window to stay on task in.</summary>
    Window,

    /// <summary>A budget of productive hours, anywhere between waking and bedtime.</summary>
    Budget,

    /// <summary>The workday's own timeline of parts stays as it is (only offered when it has one).</summary>
    KeepTimeline,
}
