namespace AccountabiliBuddy.App;

/// <param name="StartMinimized">Start in the tray without showing the window (used when launched at sign-in).</param>
public sealed record StartupOptions(bool StartMinimized)
{
    public const string MinimizedArgument = "--minimized";

    public static StartupOptions Parse(IEnumerable<string> args)
    {
        bool minimized = args.Any(arg => string.Equals(arg, MinimizedArgument, StringComparison.OrdinalIgnoreCase));
        return new StartupOptions(minimized);
    }
}
