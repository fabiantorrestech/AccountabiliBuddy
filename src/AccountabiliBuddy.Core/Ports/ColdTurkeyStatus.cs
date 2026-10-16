namespace AccountabiliBuddy.Core.Ports;

/// <param name="BrowsersWithExtensionHost">Browsers where Cold Turkey's native messaging host is registered.</param>
public sealed record ColdTurkeyStatus(
    bool IsInstalled,
    string? InstallFolder,
    string? Version,
    bool IsRunning,
    IReadOnlyList<BrowserKind> BrowsersWithExtensionHost)
{
    public static ColdTurkeyStatus NotInstalled { get; } = new(false, null, null, false, Array.Empty<BrowserKind>());
}
