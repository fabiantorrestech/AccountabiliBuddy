namespace AccountabiliBuddy.App.Services;

/// <summary>How updates are installed (documents "updates").</summary>
/// <param name="AutoInstall">Install a new version by itself when no session is running; off = only tell the user.</param>
/// <param name="CheckForUpdates">False (the Local only type): no automatic check; Check now still asks when clicked.</param>
public sealed record UpdateSettings(bool AutoInstall, bool CheckForUpdates = true)
{
    public static UpdateSettings Default { get; } = new(AutoInstall: false);
}
