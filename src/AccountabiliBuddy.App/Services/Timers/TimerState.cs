namespace AccountabiliBuddy.App.Services.Timers;

/// <summary>Where a timer is.</summary>
public enum TimerState
{
    /// <summary>Counting down to its end time.</summary>
    Running,

    /// <summary>Stopped with some time left (also after Reset: its whole length).</summary>
    Paused,

    /// <summary>Its time is up (or it was stopped).</summary>
    Done,
}
