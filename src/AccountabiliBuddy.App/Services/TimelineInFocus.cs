namespace AccountabiliBuddy.App.Services;

/// <summary>What the on-screen timeline does while the session timer shows.</summary>
public enum TimelineInFocus
{
    /// <summary>The timeline steps aside and the timer takes its place until the session ends.</summary>
    SwapForTimer,

    /// <summary>The timeline stays, with the timer under it.</summary>
    ShowBoth,
}
