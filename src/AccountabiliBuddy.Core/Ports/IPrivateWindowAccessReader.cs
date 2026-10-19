namespace AccountabiliBuddy.Core.Ports;

/// <summary>
/// Reads from a browser's own settings whether the extension may run in private windows, so it is known even while
/// the browser is closed (the extension can only report it while it runs). Read-only.
/// </summary>
public interface IPrivateWindowAccessReader
{
    PrivateWindowAccess Read(BrowserKind browser);
}
