namespace AccountabiliBuddy.Core.Ports;

/// <summary>Receives active-tab updates from the browser extension (via the native messaging host).</summary>
public interface IBrowserActivitySource
{
    event EventHandler<BrowserActivity>? ActiveTabChanged;

    void Start();

    void Stop();
}
