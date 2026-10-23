namespace AccountabiliBuddy.Core.Ports;

/// <summary>Where the mouse pointer is on screen, in pixels.</summary>
public interface ICursorPosition
{
    /// <returns>The pointer's position, or null when it can't be read (e.g. on the secure desktop).</returns>
    (int X, int Y)? Get();
}
