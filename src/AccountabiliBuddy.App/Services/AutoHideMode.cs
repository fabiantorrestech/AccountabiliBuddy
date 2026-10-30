namespace AccountabiliBuddy.App.Services;

/// <summary>
/// What the overlay does while the user's taskbar is set to hide itself (stored as a number, keep the order). While the bar is up, all three
/// show a floating tile lifted clear above it (the docked placements are not used with an auto-hiding bar); the modes differ while it is hidden.
/// </summary>
public enum AutoHideMode
{
    /// <summary>A floating tile at the bottom right while the taskbar is hidden; it lifts above the bar while it is up.</summary>
    Float,

    /// <summary>A 3 px progress line along the bottom edge with the minutes at the right while the taskbar is hidden; the tile shows above the bar while it is up.</summary>
    SlimLine,

    /// <summary>Nothing on screen while the taskbar is hidden: the tile comes up, above the bar, only while the bar is up.</summary>
    HideWithIt,
}
