using AccountabiliBuddy.App.DevTools;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Platform.Windows.Activity;
using AccountabiliBuddy.Platform.Windows.Browser;
using AccountabiliBuddy.Platform.Windows.Display;
using AccountabiliBuddy.Platform.Windows.Enforcement;
using AccountabiliBuddy.Platform.Windows.Hotkeys;
using AccountabiliBuddy.Platform.Windows.Shortcuts;
using AccountabiliBuddy.Platform.Windows.Integrations;
using AccountabiliBuddy.Platform.Windows.Ocr;
using AccountabiliBuddy.Platform.Windows.Security;
using AccountabiliBuddy.Platform.Windows.Startup;
using AccountabiliBuddy.Platform.Windows.Time;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;

namespace AccountabiliBuddy.App;

/// <summary>Composition root: builds services and view models, owns the tray icon and window lifecycle.</summary>
public partial class App : Application
{
    private const string DataDirectoryVariable = "ACCOUNTABILIBUDDY_DATA_DIR";

    // Test runs point Google sign-in and the Calendar API at a local stand-in (e.g. http://127.0.0.1:5199/).
    private const string GoogleTestBaseVariable = "ACCOUNTABILIBUDDY_GOOGLE_TEST_BASE";

    // "1" shows the Activity (dev) tab; it is hidden otherwise.
    private const string DevToolsVariable = "ACCOUNTABILIBUDDY_DEV_TOOLS";

    // Opening the window refreshes calendars, but not more often than this.
    private static readonly TimeSpan WindowRefreshInterval = TimeSpan.FromMinutes(1);
    private static readonly Uri IconUri = new("avares://AccountabiliBuddy.App/Assets/app-icon.png");

    private readonly StartupOptions _options;

    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private ConfigurationService? _configuration;
    private SessionController? _sessions;
    private SessionEndFlow? _endFlow;
    private SessionCheckpointService? _checkpoints;
    private ClockAnchorService? _clockAnchors;
    private WatchedBrowsersService? _watchedBrowsers;
    private UnlockService? _unlocks;
    private CalendarNetwork? _calendarNetwork;
    private CalendarSyncService? _calendarSync;
    private DateTimeOffset _lastWindowRefresh = DateTimeOffset.MinValue;
    private WinEventActivityWatcher? _activityWatcher;
    private NamedPipeBrowserSource? _browserSource;
    private Win32GlobalHotkey? _hotkey;
    private QuickSessionService? _quickSession;
    private InstanceSignals? _instanceSignals;
    private ActivityRecorder? _recorder;
    private EnforcementCoordinator? _enforcement;
    private MainWindow? _mainWindow;
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _endSessionMenuItem;

    // Used by the XAML previewer.
    public App()
        : this(new StartupOptions(StartMinimized: false))
    {
    }

    public App(StartupOptions options)
    {
        _options = options;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            // The app lives in the tray; only Quit ends it.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) => DisposeServices();

            BuildServicesAndWindow();
            CreateTrayIcon();

            if (_options.QuickSession)
            {
                _quickSession!.Show();
            }
            else if (!_options.StartMinimized)
            {
                ShowMainWindow();
            }

            // Later launches (second start, Start menu shortcut) hand over to this copy.
            _instanceSignals = InstanceSignals.Listen(Program.InstanceName, signal => Dispatcher.UIThread.Post(() =>
            {
                if (signal == InstanceSignal.QuickSession)
                {
                    _quickSession?.Show();
                }
                else
                {
                    ShowMainWindow();
                }
            }));
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void BuildServicesAndWindow()
    {
        // Changing the Windows clock can't move sessions, waits or countdowns (see TrustedClock).
        var time = new TrustedClock(TimeProvider.System, () => Environment.TickCount64, new RegistryBootIdProvider().GetBootId());

        // Override lets test runs use a throwaway database instead of the real one.
        string dataDirectory = Environment.GetEnvironmentVariable(DataDirectoryVariable) ?? LocalDatabase.DefaultDirectory();
        var database = new LocalDatabase(dataDirectory);
        var documents = new SqliteJsonDocumentStore(database, time);
        _clockAnchors = new ClockAnchorService(time, documents);
        _clockAnchors.RestoreBlocking();
        _configuration = new ConfigurationService(new SqliteProfileStore(database, time), documents, time);
        _configuration.LoadBlocking();

        _sessions = new SessionController(new SessionEngine(time), _configuration, time);
        _endFlow = new SessionEndFlow(_sessions);

        var startupRegistrar = new RunKeyStartupRegistrar(
            Environment.ProcessPath ?? throw new InvalidOperationException("Executable path is unknown."),
            StartupOptions.MinimizedArgument);

        _browserSource = new NamedPipeBrowserSource(time);
        var browserSetup = new WindowsBrowserSetup(
            Path.Combine(AppContext.BaseDirectory, "AccountabiliBuddy.NativeHost.exe"),
            Path.Combine(AppContext.BaseDirectory, "browser-extension"),
            dataDirectory);
        _checkpoints = new SessionCheckpointService(_sessions, documents);
        SavedSession? savedSession = _checkpoints.LoadBlocking();
        // Loaded before the sessions start, so an unlock in effect keeps scheduled blocks from starting at launch.
        _unlocks = new UnlockService(_sessions, documents);
        _unlocks.LoadBlocking();
        _watchedBrowsers = new WatchedBrowsersService(_browserSource, documents);
        _watchedBrowsers.LoadBlocking();
        var sightings = new ColdTurkeySightingsService(_browserSource, documents, time);
        _ = sightings.LoadAsync();

        var monitorLocator = new Win32MonitorLocator();
        _hotkey = new Win32GlobalHotkey();
        _quickSession = new QuickSessionService(
            _sessions, _configuration, _endFlow, monitorLocator, new GdiScreenCapture(), _hotkey, documents);
        _quickSession.LoadBlocking();
        // M6 screen reading and M7 activity history.
        var privacy = new PrivacySettingsService(documents);
        privacy.LoadBlocking();
        var ocrReader = new WindowsOcrReader();
        var activityLog = new SqliteActivityLog(database, new DpapiDataProtector());
        _recorder = new ActivityRecorder(activityLog, privacy, _sessions, new Win32UserIdleDetector(), time);

        var quickLaunch = new StartMenuQuickLaunchShortcut(
            Environment.ProcessPath ?? throw new InvalidOperationException("Executable path is unknown."),
            StartupOptions.QuickSessionArgument);

        var activityViewModel = new ActivityDebugViewModel();
        _activityWatcher = new WinEventActivityWatcher(time);
        // The watcher raises events on its own thread; UI collections must be touched on the UI thread.
        _activityWatcher.ActivityChanged += (_, snapshot) => Dispatcher.UIThread.Post(() => activityViewModel.Apply(snapshot));

        var presenter = new EnforcementPresenter(new WindowsEnforcer(), _browserSource, monitorLocator, time);
        _enforcement = new EnforcementCoordinator(
            _activityWatcher,
            _browserSource,
            _sessions,
            _configuration,
            new EnforcementEngine(new RuleEvaluator(new RuleMatcher())),
            presenter,
            ocrReader,
            privacy,
            _recorder,
            _watchedBrowsers);

        var pendingChanges = new PendingChangesViewModel(_configuration, _sessions);
        var calendars = new CalendarService(documents);
        calendars.LoadBlocking();
        var dataProtector = new DpapiDataProtector();
        _calendarNetwork = new CalendarNetwork();
        _calendarSync = new CalendarSyncService(
            calendars,
            _calendarNetwork,
            new GoogleCalendarClient(_calendarNetwork, GoogleEndpointsFromEnvironment(), time),
            dataProtector,
            time);
        var mainViewModel = new MainWindowViewModel(
            new SessionViewModel(
                _sessions, _configuration, _endFlow, VisibleMainWindow, new UnlockViewModel(_sessions, _configuration, _unlocks)),
            new ProfilesViewModel(_configuration, _sessions, pendingChanges),
            new ScheduleViewModel(_configuration, _sessions, pendingChanges),
            new ExceptionsViewModel(_configuration, _sessions, calendars, _calendarSync, OpenInBrowser, pendingChanges),
            new HistoryViewModel(activityLog, _recorder, _configuration, privacy, time),
            new SetupViewModel(
                startupRegistrar,
                quickLaunch,
                browserSetup,
                _browserSource,
                new PrivacySetupViewModel(privacy, _recorder, ocrReader.IsAvailable),
                _sessions,
                _watchedBrowsers),
            new IntegrationsViewModel(new ColdTurkeyDetector(), sightings, _configuration, _sessions),
            new SettingsViewModel(_configuration, _sessions, _quickSession, calendars, dataProtector, pendingChanges, database.DatabasePath),
            activityViewModel,
            Environment.GetEnvironmentVariable(DevToolsVariable) == "1",
            _configuration.LoadError);

        _mainWindow = new MainWindow { DataContext = mainViewModel };

        mainViewModel.ShowSystemClockOffset(time.SystemClockOffset);
        time.SystemClockOffsetChanged += (_, offset) => Dispatcher.UIThread.Post(() =>
        {
            mainViewModel.ShowSystemClockOffset(offset);
            if (offset != TimeSpan.Zero)
            {
                presenter.ShowNotice("Windows clock changed", mainViewModel.ClockNotice ?? string.Empty);
            }
        });

        _sessions.StateChanged += (_, _) => UpdateTray();
        _sessions.Ticked += (_, _) => UpdateTray();

        // Looser changes take effect once their countdown is over and no session is running.
        _sessions.StateChanged += (_, state) => _ = _configuration.ApplyPendingIfDueAsync(state.IsActive);
        _sessions.Ticked += (_, _) => _ = _configuration.ApplyPendingIfDueAsync(_sessions.State.IsActive);

        _recorder.Start();
        SessionResume? resumed = _sessions.Start(savedSession);
        if (resumed is not null && _sessions.State.IsActive)
        {
            presenter.ShowNotice(
                "Session resumed",
                $"AccountabiliBuddy was closed for {DescribeGap(resumed.ClosedFor)}. Your session kept running.");
        }
        _activityWatcher.Start();
        _browserSource.Start();
        _calendarSync.Start();
        _clockAnchors.Start();
    }

    private void CreateTrayIcon()
    {
        var quickItem = new NativeMenuItem($"Quick session…  ({_quickSession!.Preferences.Hotkey})");
        quickItem.Click += (_, _) => _quickSession.Show();

        var openItem = new NativeMenuItem("Open AccountabiliBuddy");
        openItem.Click += (_, _) => ShowMainWindow();

        _endSessionMenuItem = new NativeMenuItem("End session…");
        _endSessionMenuItem.Click += async (_, _) => await _endFlow!.TryEndSessionAsync(VisibleMainWindow());

        var quitItem = new NativeMenuItem("Quit");
        quitItem.Click += async (_, _) => await QuitAsync();

        var menu = new NativeMenu();
        menu.Items.Add(quickItem);
        menu.Items.Add(openItem);
        menu.Items.Add(_endSessionMenuItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quitItem);

        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(IconUri)),
            ToolTipText = "AccountabiliBuddy",
            Menu = menu,
        };
        _trayIcon.Clicked += (_, _) => ShowMainWindow();

        TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
        UpdateTray();
    }

    private void UpdateTray()
    {
        if (_trayIcon is null || _sessions is null || _configuration is null)
        {
            return;
        }

        SessionState state = _sessions.State;
        string? profileName = ProfileOptions.NameOf(_configuration.Current.Profiles, state.ProfileId);

        _trayIcon.ToolTipText = state.IsActive
            ? $"AccountabiliBuddy · {SessionStatusFormatter.Title(state, profileName)} · {SessionStatusFormatter.Detail(state, _sessions.Now)}"
            : "AccountabiliBuddy · no session running";

        if (_endSessionMenuItem is not null)
        {
            _endSessionMenuItem.IsEnabled = state.IsActive;
        }
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();

        if (_calendarSync is not null && DateTimeOffset.UtcNow - _lastWindowRefresh >= WindowRefreshInterval)
        {
            _lastWindowRefresh = DateTimeOffset.UtcNow;
            _ = _calendarSync.RefreshAllAsync();
        }
    }

    private static bool OpenInBrowser(Uri uri)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static GoogleEndpoints GoogleEndpointsFromEnvironment()
    {
        string? testBase = Environment.GetEnvironmentVariable(GoogleTestBaseVariable);
        if (testBase is null || !Uri.TryCreate(testBase, UriKind.Absolute, out Uri? baseUri) || !baseUri.IsLoopback)
        {
            return GoogleEndpoints.Default;
        }

        return new GoogleEndpoints(
            new Uri(baseUri, "auth"), new Uri(baseUri, "token"), new Uri(baseUri, "revoke"), new Uri(baseUri, "calendar/v3/"));
    }

    private Window? VisibleMainWindow()
    {
        return _mainWindow is { IsVisible: true } ? _mainWindow : null;
    }

    private static string DescribeGap(TimeSpan gap)
    {
        return gap.TotalMinutes < 1 ? "less than a minute"
            : gap.TotalHours < 1 ? $"{(int)gap.TotalMinutes} min"
            : $"{(int)gap.TotalHours} h {gap.Minutes} min";
    }

    private async Task QuitAsync()
    {
        // Quitting ends the running session, so it is not resumed at the next launch.
        // During focus that is the same as ending it early (friction); in a break it just ends.
        if (_sessions is { State.IsActive: true } && !await _endFlow!.TryEndSessionAsync(VisibleMainWindow()))
        {
            return;
        }

        if (_mainWindow is not null)
        {
            _mainWindow.AllowClose = true;
        }

        _desktop?.Shutdown();
    }

    private void DisposeServices()
    {
        _instanceSignals?.Dispose();
        _hotkey?.Dispose();
        _enforcement?.Dispose();
        _recorder?.Dispose();
        _watchedBrowsers?.Dispose();
        _browserSource?.Dispose();
        _activityWatcher?.Dispose();
        _checkpoints?.Dispose();
        _clockAnchors?.Dispose();
        _unlocks?.Dispose();
        _calendarSync?.Dispose();
        _calendarNetwork?.Dispose();
        _sessions?.Dispose();
        _trayIcon?.Dispose();
    }
}
