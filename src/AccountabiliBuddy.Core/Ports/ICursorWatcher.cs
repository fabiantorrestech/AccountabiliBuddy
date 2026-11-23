namespace AccountabiliBuddy.Core.Ports;

/// <summary>
/// Tells when the mouse pointer goes into or out of one area of the screen, whichever app is under it, from the mouse's own events (no
/// polling; nothing runs while the mouse is still). Nothing is installed while no area is watched. Used by the overlay so its kept-solid
/// time and date dim under the pointer, though the overlay itself lets clicks through and never sees the mouse.
/// </summary>
public interface ICursorWatcher
{
    /// <summary>True while the pointer is inside the watched area.</summary>
    bool IsInside { get; }

    /// <summary>Raised with the new state when the pointer goes in or out. Raised on the watcher's own thread.</summary>
    event EventHandler<bool>? InsideChanged;

    /// <summary>Watches this area (physical screen pixels), or stops watching when null. The same area again changes nothing.</summary>
    void Watch(PixelBox? area);
}
