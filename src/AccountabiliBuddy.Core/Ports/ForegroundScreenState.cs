namespace AccountabiliBuddy.Core.Ports;

/// <summary>Whether the window in front covers a whole monitor, which monitor, and whether it looks like a game.</summary>
/// <param name="IsFullScreen">Another app's window with no title bar covers its whole monitor (a video, a presentation, a game).</param>
/// <param name="IsGame">Full screen with no taskbar over it and drawn by Direct3D exclusive mode (Windows' own "running a full-screen game" state).</param>
/// <param name="MonitorId">The monitor it covers; null when not full screen.</param>
/// <param name="ProcessName">The app's process name without ".exe" (for the per-app rules); null when not full screen.</param>
public sealed record ForegroundScreenState(bool IsFullScreen, bool IsGame, string? MonitorId, string? ProcessName)
{
    public static ForegroundScreenState None { get; } = new(false, false, null, null);
}
