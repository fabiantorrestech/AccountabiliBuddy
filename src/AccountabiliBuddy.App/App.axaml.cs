using AccountabiliBuddy.App.DevTools;
using AccountabiliBuddy.Platform.Windows.Activity;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace AccountabiliBuddy.App;

public partial class App : Application
{
    private WinEventActivityWatcher? _activityWatcher;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // M2 dev view: shows live watcher output. M3 replaces this with the tray shell.
            var viewModel = new ActivityDebugViewModel();

            _activityWatcher = new WinEventActivityWatcher(TimeProvider.System);
            // The watcher raises events on its own thread; UI collections must be touched on the UI thread.
            _activityWatcher.ActivityChanged += (_, snapshot) => Dispatcher.UIThread.Post(() => viewModel.Apply(snapshot));

            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            desktop.Exit += (_, _) => _activityWatcher.Dispose();

            _activityWatcher.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
