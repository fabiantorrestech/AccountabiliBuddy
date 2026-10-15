namespace AccountabiliBuddy.Core.Ports;

/// <summary>Carries out the Overlay and Enforce escalation levels.</summary>
public interface IEnforcer
{
    Task ShowOverlayAsync(string monitorId, string message, CancellationToken cancellationToken = default);

    Task HideOverlayAsync(string monitorId, CancellationToken cancellationToken = default);

    Task MinimizeWindowAsync(string windowId, CancellationToken cancellationToken = default);

    /// <summary>Sends the tab to the extension's block page.</summary>
    Task RedirectBrowserTabAsync(string tabId, CancellationToken cancellationToken = default);
}
