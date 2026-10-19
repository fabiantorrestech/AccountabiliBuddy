namespace AccountabiliBuddy.App;

/// <param name="StartMinimized">Start in the tray without showing the window (used when launched at sign-in).</param>
/// <param name="QuickSession">Open the quick-session popup (used by the Start menu shortcut).</param>
/// <param name="Restarted">Started by the app itself to reload restored settings: waits for the old copy to exit.</param>
public sealed record StartupOptions(bool StartMinimized, bool QuickSession = false, bool Restarted = false)
{
    public const string MinimizedArgument = "--minimized";
    public const string QuickSessionArgument = "--quick";
    public const string RestartedArgument = "--restarted";

    public static StartupOptions Parse(IEnumerable<string> args)
    {
        string[] all = args.ToArray();
        return new StartupOptions(Has(all, MinimizedArgument), Has(all, QuickSessionArgument), Has(all, RestartedArgument));
    }

    private static bool Has(string[] args, string argument)
    {
        return args.Any(arg => string.Equals(arg, argument, StringComparison.OrdinalIgnoreCase));
    }
}
