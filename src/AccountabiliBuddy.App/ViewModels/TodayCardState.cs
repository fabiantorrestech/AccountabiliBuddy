namespace AccountabiliBuddy.App.ViewModels;

/// <summary>What Today's main card shows: one thing at a time.</summary>
public enum TodayCardState
{
    /// <summary>The "Wrap up the day" card after the day's last work part.</summary>
    Shutdown,

    /// <summary>No profiles yet: the first-profile welcome.</summary>
    Welcome,

    /// <summary>Nothing runs: the next session's tasks and Start.</summary>
    Idle,

    /// <summary>A session's planning stage (or a first session's learning).</summary>
    Planning,

    /// <summary>A session runs.</summary>
    Running,
}
