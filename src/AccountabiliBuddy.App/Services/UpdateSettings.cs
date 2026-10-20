namespace AccountabiliBuddy.App.Services;

/// <summary>How updates are installed (documents "updates").</summary>
/// <param name="AutoInstall">Install a new version by itself when no session is running; off = only tell the user.</param>
public sealed record UpdateSettings(bool AutoInstall)
{
    public static UpdateSettings Default { get; } = new(AutoInstall: false);
}
