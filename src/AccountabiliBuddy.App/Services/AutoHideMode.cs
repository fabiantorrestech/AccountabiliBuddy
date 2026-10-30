namespace AccountabiliBuddy.App.Services;

/// <summary>What the overlay does while the user's taskbar is set to hide itself (stored as a number, keep the order).</summary>
public enum AutoHideMode
{
    /// <summary>A floating tile at the bottom right until the taskbar comes back.</summary>
    Float,

    /// <summary>A 3 px progress line along the bottom edge with the minutes at the right.</summary>
    SlimLine,

    /// <summary>Nothing on screen: it comes up with the taskbar.</summary>
    HideWithIt,
}
