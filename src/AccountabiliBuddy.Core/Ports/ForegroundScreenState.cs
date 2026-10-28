namespace AccountabiliBuddy.Core.Ports;

/// <summary>The window in front: which app, whether it covers a whole monitor, and whether it looks like a game.</summary>
/// <param name="IsFullScreen">Another app's window with no title bar covers its whole monitor (a video, a presentation, a game).</param>
/// <param name="IsGame">Full screen with no taskbar over it and drawn by Direct3D exclusive mode (Windows' own "running a full-screen game" state).</param>
/// <param name="MonitorId">The monitor it is on (the one it covers when full screen); null with no other app in front.</param>
/// <param name="ProcessName">The app's process name without ".exe" (for the per-app rules); null with no other app in front.</param>
/// <param name="ClassName">The window's class name (a call window is recognised by class, never by title); null with no other app in front.</param>
public sealed record ForegroundScreenState(bool IsFullScreen, bool IsGame, string? MonitorId, string? ProcessName, string? ClassName = null)
{
    public static ForegroundScreenState None { get; } = new(false, false, null, null);
}
