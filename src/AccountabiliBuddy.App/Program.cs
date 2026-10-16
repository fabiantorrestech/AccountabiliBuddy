using Avalonia;

namespace AccountabiliBuddy.App;

public static class Program
{
    private const string InstanceName = "AccountabiliBuddy";

    [STAThread]
    public static void Main(string[] args)
    {
        using SingleInstanceGuard? guard = SingleInstanceGuard.TryAcquire(InstanceName);
        if (guard is null)
        {
            // Already running (lives in the tray).
            return;
        }

        StartupOptions options = StartupOptions.Parse(args);
        BuildAvaloniaApp(options).StartWithClassicDesktopLifetime(args);
    }

    // Parameterless overload is used by the Avalonia XAML previewer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return BuildAvaloniaApp(new StartupOptions(StartMinimized: false));
    }

    private static AppBuilder BuildAvaloniaApp(StartupOptions options)
    {
        return AppBuilder.Configure(() => new App(options))
            .UsePlatformDetect()
            .LogToTrace();
    }
}
