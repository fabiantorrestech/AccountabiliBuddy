namespace AccountabiliBuddy.Core.Ports;

/// <summary>
/// OS-level enforcement actions. Toasts and overlays are drawn by the app's UI layer;
/// browser tab redirects arrive with the extension (M5).
/// </summary>
public interface IEnforcer
{
    /// <summary>Minimizes a window. Does nothing if the window no longer exists.</summary>
    Task MinimizeWindowAsync(string windowId, CancellationToken cancellationToken = default);
}
