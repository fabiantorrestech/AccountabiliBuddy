namespace AccountabiliBuddy.Core.Ports;

/// <summary>
/// Receives active-tab reports from the browser extension (through the native messaging host, ADR 5)
/// and sends commands back to it.
/// </summary>
public interface IBrowserActivitySource
{
    /// <summary>Raised when a browser connects, disconnects or reports new tabs. May be raised on any thread.</summary>
    event EventHandler? ConnectionsChanged;

    /// <summary>Currently connected browsers.</summary>
    IReadOnlyList<BrowserConnection> Connections { get; }

    void Start();

    void Stop();

    /// <summary>Sends a tab to the extension's block page. Does nothing if the browser is no longer connected.</summary>
    /// <param name="reason">Shown on the block page, e.g. which profile and rule blocked it.</param>
    Task RedirectToBlockPageAsync(string tabId, string reason, CancellationToken cancellationToken = default);
}
