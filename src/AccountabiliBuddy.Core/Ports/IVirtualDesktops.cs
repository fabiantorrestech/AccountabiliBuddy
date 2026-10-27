namespace AccountabiliBuddy.Core.Ports;

/// <summary>
/// Windows virtual desktops, for cards that must be in front of the user wherever they are: asks whether windows are on the desktop
/// the user is looking at, and brings them along when the user has switched to another one. Only this app's own windows can be moved.
/// </summary>
public interface IVirtualDesktops
{
    /// <returns>True when the window is on the current desktop; also when that can't be told (nothing to fix then).</returns>
    bool IsOnCurrentDesktop(nint windowHandle);

    /// <summary>Moves the windows to the desktop the user is looking at.</summary>
    /// <returns>False when that desktop couldn't be found (e.g. it has no other window yet); try again later.</returns>
    bool MoveToCurrentDesktop(IReadOnlyList<nint> windowHandles);
}
