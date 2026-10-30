namespace AccountabiliBuddy.App.Services;

/// <summary>What the overlay is drawn as right now.</summary>
public enum OverlayTargetKind
{
    /// <summary>Nothing on screen.</summary>
    Hidden,

    /// <summary>A tile over a horizontal taskbar.</summary>
    Docked,

    /// <summary>A tile floating in the bottom right or bottom left of the work area.</summary>
    Floating,

    /// <summary>The vertical tile beside a taskbar docked on a side of the screen (8d).</summary>
    VerticalTile,

    /// <summary>A 3 px progress line along the bottom edge with the minutes at the right (auto-hidden taskbar).</summary>
    SlimLine,
}
