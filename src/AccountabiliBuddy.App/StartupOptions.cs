namespace AccountabiliBuddy.App;

/// <param name="StartMinimized">Start in the tray without showing the window (used when launched at sign-in).</param>
/// <param name="QuickSession">Open the quick-session popup (used by the Start menu shortcut).</param>
public sealed record StartupOptions(bool StartMinimized, bool QuickSession = false)
{
    public const string MinimizedArgument = "--minimized";
    public const string QuickSessionArgument = "--quick";

    public static StartupOptions Parse(IEnumerable<string> args)
    {
        string[] all = args.ToArray();
        return new StartupOptions(Has(all, MinimizedArgument), Has(all, QuickSessionArgument));
    }

    private static bool Has(string[] args, string argument)
    {
        return args.Any(arg => string.Equals(arg, argument, StringComparison.OrdinalIgnoreCase));
    }
}
