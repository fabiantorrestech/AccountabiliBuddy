using AccountabiliBuddy.Platform.Windows.Shortcuts;
using Avalonia;

namespace AccountabiliBuddy.App;

public static class Program
{
    public const string InstanceName = "AccountabiliBuddy";

    [STAThread]
    public static void Main(string[] args)
    {
        StartupOptions options = StartupOptions.Parse(args);

        using SingleInstanceGuard? guard = SingleInstanceGuard.TryAcquire(InstanceName);
        if (guard is null)
        {
            // Already running (lives in the tray): hand over to it. This launch was a user action, so it may
            // pass on the right to bring a window to the front.
            ForegroundHandOff.AllowAnyProcess();
            InstanceSignals.Send(InstanceName, options.QuickSession ? InstanceSignal.QuickSession : InstanceSignal.Show);
            return;
        }

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
