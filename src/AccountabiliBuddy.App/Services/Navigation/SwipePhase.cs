namespace AccountabiliBuddy.App.Services.Navigation;

/// <summary>Where a two-finger swipe has got to (see <see cref="SwipeTracker"/>).</summary>
public enum SwipePhase
{
    /// <summary>No swipe going on.</summary>
    Idle,

    /// <summary>A swipe is building up toward the threshold: the bubble shows and follows <see cref="SwipeState.Progress"/>.</summary>
    Tracking,

    /// <summary>The swipe reached the threshold just now: navigate once.</summary>
    Commit,

    /// <summary>The swipe stopped (a pause) before the threshold: the bubble goes away, nothing navigates.</summary>
    Cancelled,
}
