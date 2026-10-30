namespace AccountabiliBuddy.App.Services;

/// <summary>How big the session overlay is (stored as a number, keep the order): one line, two lines, two lines plus a row, or wide with the clock and batteries.</summary>
public enum OverlaySize
{
    /// <summary>One line, 36 px tall.</summary>
    Small,

    /// <summary>Two lines, as tall as the taskbar (the default).</summary>
    Medium,

    /// <summary>Medium plus a sound / timeline row; rises 36 px above the taskbar.</summary>
    Large,

    /// <summary>Session, clock and date, and batteries side by side.</summary>
    Wide,
}
