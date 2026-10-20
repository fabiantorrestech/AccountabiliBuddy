using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Ports;

/// <summary>
/// What the on-screen session timer needs from the OS: letting clicks pass through its window, noticing the keys
/// that make it draggable while another app has the keyboard, noticing the mouse over it (it gets no mouse events
/// while clicks pass through), and noticing a fullscreen app in front.
/// </summary>
public interface IOverlayWindowControl
{
    /// <summary>
    /// Makes the window ignore the mouse, so clicks reach whatever is under it, or take the mouse again.
    /// Either way the window never takes focus from the app being used.
    /// </summary>
    void SetClickThrough(nint windowHandle, bool clickThrough);

    /// <summary>True while Ctrl and Alt are both held down, whichever app is in front.</summary>
    bool AreDragKeysHeld();

    /// <summary>True while the mouse pointer is over the window, also while clicks pass through it.</summary>
    bool IsCursorOver(nint windowHandle);

    /// <summary>
    /// True when another app's window in front covers its whole screen (a fullscreen video, presentation or game).
    /// The desktop and this app's own windows don't count.
    /// </summary>
    bool IsFullscreenAppInFront();

    /// <summary>
    /// Another app's maximized window in front, title bar and all (Word, a browser…): which app and its screen (whole
    /// monitor); null when the window in front isn't maximized. The desktop and this app's own windows don't count.
    /// </summary>
    MaximizedApp? MaximizedAppInFront();

    /// <summary>
    /// Where the main taskbar is now (physical pixels), also while an auto-hiding taskbar is slid away (mostly off
    /// screen then); null when there is none.
    /// </summary>
    ScreenBox? TaskbarBox();
}
