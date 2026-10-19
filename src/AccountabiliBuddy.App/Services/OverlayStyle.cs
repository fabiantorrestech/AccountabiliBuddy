namespace AccountabiliBuddy.App.Services;

/// <summary>How the on-screen session timer looks.</summary>
public enum OverlayStyle
{
    /// <summary>A thin bar that empties as the session goes on, with the end time in small print (the default).</summary>
    BarWithEndTime,

    /// <summary>"Coding · 42:17 left" over the bar.</summary>
    TimeLeftWithBar,

    /// <summary>Just "42:17 left".</summary>
    TimeLeftOnly,

    /// <summary>A countdown ring with the time left in the middle.</summary>
    Ring,
}
