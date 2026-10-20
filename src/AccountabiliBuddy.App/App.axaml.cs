using AccountabiliBuddy.App.DevTools;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.Services.Backup;
using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.App.Views;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Platform.Windows.Activity;
using AccountabiliBuddy.Platform.Windows.Audio;
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
    private const string DevDataSuffix = "-Dev";

    // True when this build isn't the installed app and so uses the separate "-Dev" data folder.
    private bool _usesDevData;

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
    private SessionOverlayService? _sessionOverlay;
    private NoisePlayerService? _noise;
    private NoiseSettingsService? _noiseSettings;
    private SessionCueService? _sessionCues;
    private AppAndSiteIcons? _icons;
    private UpdateService? _updates;
    private Win32SoundPlayer? _soundPlayer;
    private DayBarViewModel? _dayBar;
    private PrivateWindowAccessMonitor? _privateAccess;
    private PrivateWindowsNagWindow? _privateWindowsNag;
    private CalendarNetwork? _calendarNetwork;
    private CalendarSyncService? _calendarSync;
    private DateTimeOffset _lastWindowRefresh = DateTimeOffset.MinValue;
    private WinEventActivityWatcher? _activityWatcher;
    private ProgramPathsService? _programPaths;
    private BedtimeReminderService? _bedtimeReminder;
    private ColdTurkeyService? _coldTurkey;
    private AccountabiliBuddy.App.Services.Link.LinkService? _link;
    private NamedPipeBrowserSource? _browserSource;
    private Win32GlobalHotkey? _hotkey;
    private QuickSessionService? _quickSession;
    private InstanceSignals? _instanceSignals;
    private ActivityRecorder? _recorder;
    private EnforcementCoordinator? _enforcement;
    private MainWindow? _mainWindow;
    private SetupAssistantService? _setupAssistant;
    private Func<SetupAssistantViewModel>? _createAssistant;
    private SetupAssistantWindow? _assistantWindow;
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

            string? problem = BuildServicesAndWindow();
            if (problem is not null)
            {
                ShowStartupProblem(problem);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            CreateTrayIcon();

            if (_options.QuickSession)
            {
                _quickSession!.Show();
            }
            else if (!_options.StartMinimized)
            {
                ShowMainWindow();

                // First launch: offer to bring over other blockers' blocks and set up browsers.
                if (_setupAssistant is { HasBeenShown: false } && _sessions is { State.IsActive: false })
                {
                    ShowSetupAssistant();
                }
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

    /// <returns>Why the app can't start (e.g. data from a newer version), or null when it started.</returns>
    private string? BuildServicesAndWindow()
    {
        // Changing the Windows clock can't move sessions, waits or countdowns (see TrustedClock).
        var time = new TrustedClock(TimeProvider.System, () => Environment.TickCount64, new RegistryBootIdProvider().GetBootId());

        // Override lets test runs use a throwaway database instead of the real one. A build that isn't the installed
        // app (dotnet run, a test copy) keeps its own data, so trying things out never mixes with real sessions.
        string? dataOverride = Environment.GetEnvironmentVariable(DataDirectoryVariable);
        _usesDevData = dataOverride is null && !AppPaths.IsInstalled;
        string dataDirectory = dataOverride ?? (_usesDevData ? LocalDatabase.DefaultDirectory() + DevDataSuffix : LocalDatabase.DefaultDirectory());
        var database = new LocalDatabase(dataDirectory);
        string? problem = UpgradeSafety.CheckBlocking(database);
        if (problem is not null)
        {
            return problem;
        }

        var documents = new SqliteJsonDocumentStore(database, time);
        // First launch of a new version: keep a copy of the data before anything migrates it.
        UpgradeSafety.BackupIfNewVersionBlocking(database, documents, BackupService.AppVersion, time);
        _clockAnchors = new ClockAnchorService(time, documents);
        _clockAnchors.RestoreBlocking();
        _configuration = new ConfigurationService(new SqliteProfileStore(database, time), documents, time);
        _configuration.LoadBlocking();

        _sessions = new SessionController(new SessionEngine(time), _configuration, time);
        _endFlow = new SessionEndFlow(_sessions);

        var startupRegistrar = new RunKeyStartupRegistrar(AppPaths.LaunchPath, StartupOptions.MinimizedArgument);

        _browserSource = new NamedPipeBrowserSource(time);
        var browserSetup = new WindowsBrowserSetup(AppPaths.NativeHostPath, AppPaths.BundledExtensionFolder, dataDirectory);
        _checkpoints = new SessionCheckpointService(_sessions, documents);
        SavedSession? savedSession = _checkpoints.LoadBlocking();
        // Loaded before the sessions start, so an unlock in effect keeps scheduled blocks from starting at launch.
        _unlocks = new UnlockService(_sessions, documents);
        _unlocks.LoadBlocking();
        _watchedBrowsers = new WatchedBrowsersService(_browserSource, documents);
        _watchedBrowsers.LoadBlocking();
        _privateAccess = new PrivateWindowAccessMonitor(new WindowsPrivateWindowAccessReader(), _browserSource, _watchedBrowsers, time);
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
        _ = FillHistorySectionsOnceAsync(documents, activityLog);

        var quickLaunch = new StartMenuQuickLaunchShortcut(AppPaths.LaunchPath, StartupOptions.QuickSessionArgument);
        // Only the installed app repoints registrations to itself; a dev build or test copy must not take over the
        // installed app's start with Windows, Start menu shortcut or browser host.
        bool extensionUpdated = AppPaths.IsInstalled && RepairRegistrations(startupRegistrar, quickLaunch, browserSetup);

        var activityViewModel = new ActivityDebugViewModel();
        _activityWatcher = new WinEventActivityWatcher(time);
        // The watcher raises events on its own thread; UI collections must be touched on the UI thread.
        _activityWatcher.ActivityChanged += (_, snapshot) => Dispatcher.UIThread.Post(() => activityViewModel.Apply(snapshot));

        var presenter = new EnforcementPresenter(new WindowsEnforcer(), _browserSource, monitorLocator, time);
        _updates = new UpdateService(
            new VelopackUpdater(), _sessions, documents, presenter.ShowNotice, () => _mainWindow?.IsVisible == true);
        _updates.LoadBlocking();
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
            _watchedBrowsers,
            _privateAccess);

        var pendingChanges = new PendingChangesViewModel(_configuration, _sessions);
        var calendars = new CalendarService(documents);
        calendars.LoadBlocking();
        var dayBarSettings = new DayBarSettingsService(documents);
        dayBarSettings.LoadBlocking();
        var overlaySettings = new OverlaySettingsService(documents);
        overlaySettings.LoadBlocking();
        var sleepSettings = new SleepSettingsService(documents);
        sleepSettings.LoadBlocking();
        _bedtimeReminder = new BedtimeReminderService(sleepSettings, documents, time);
        _bedtimeReminder.LoadBlocking();
        var omittedSessions = new OmittedSessionsService(documents);
        omittedSessions.LoadBlocking();
        var displayNames = new DisplayNamesService(documents);
        displayNames.LoadBlocking();
        var hiddenUsage = new HiddenUsageService(documents);
        hiddenUsage.LoadBlocking();
        var hiddenDistractions = new HiddenUsageService(documents, HiddenUsageService.DistractionsKey);
        hiddenDistractions.LoadBlocking();
        var deletedUsage = new DeletedUsageService(documents);
        deletedUsage.LoadBlocking();
        _icons = new AppAndSiteIcons(Path.Combine(dataDirectory, "icons"), new Win32AppIconReader());
        var desktopShell = new Win32DesktopShell();
        _programPaths = new ProgramPathsService(documents, desktopShell);
        _programPaths.LoadBlocking();
        var timelineDay = new DayBarViewModel(
            _configuration, _sessions, calendars, activityLog, dayBarSettings, sleepSettings, omittedSessions, time, ignoresShowSetting: true);
        var soundSettings = new SessionSoundSettingsService(documents);
        soundSettings.LoadBlocking();
        _soundPlayer = new Win32SoundPlayer();
        _sessionCues = new SessionCueService(_sessions, soundSettings, _soundPlayer);
        _sessionOverlay = new SessionOverlayService(
            _sessions, _configuration, overlaySettings, new Win32OverlayWindowControl(), timelineDay, _sessionCues,
            () => _quickSession?.CountUpProfileId);
        _dayBar = new DayBarViewModel(_configuration, _sessions, calendars, activityLog, dayBarSettings, sleepSettings, omittedSessions, time);
        _noiseSettings = new NoiseSettingsService(documents);
        _noiseSettings.LoadBlocking();
        _noise = new NoisePlayerService(
            () => new AccountabiliBuddy.Platform.Windows.Audio.WindowsNoisePlayer(), _noiseSettings, () => new Win32GlobalHotkey());
        _noise.Start();
        var noisePlayer = new NoisePlayerViewModel(_noise);
        _sessionOverlay.SetNoisePlayer(noisePlayer);
        var dataProtector = new DpapiDataProtector();
        _calendarNetwork = new CalendarNetwork();
        _calendarSync = new CalendarSyncService(
            calendars,
            _calendarNetwork,
            new GoogleCalendarClient(_calendarNetwork, GoogleEndpointsFromEnvironment(), time),
            dataProtector,
            time);
        // Linked devices (ADR 14): off until turned on in Setup.
        var linkHost = new AccountabiliBuddy.App.Services.Link.LinkSessionHost(
            _sessions, _configuration, _quickSession, documents, calendars, sleepSettings, dayBarSettings);
        _link = new AccountabiliBuddy.App.Services.Link.LinkService(
            documents, new DpapiDataProtector(), linkHost, _sessions, new AccountabiliBuddy.Platform.Windows.Network.WindowsNetworkTrust());
        _link.LoadBlocking();

        var setupViewModel = new SetupViewModel(
            startupRegistrar,
            quickLaunch,
            browserSetup,
            _browserSource,
            new PrivacySetupViewModel(privacy, _recorder, ocrReader.IsAvailable),
            _sessions,
            _watchedBrowsers,
            _privateAccess)
        {
            LinkedDevices = new LinkedDevicesViewModel(_link, _sessions),
        };
        _setupAssistant = new SetupAssistantService(documents, time);
        _setupAssistant.LoadBlocking();
        var blockerLocator = new WindowsBlockerSourceLocator();
        ConfigurationService configuration = _configuration;
        SessionController sessions = _sessions;
        var backups = new BackupService(_configuration, documents, activityLog, new DpapiDataProtector(), time, dataDirectory);
        _createAssistant = () => new SetupAssistantViewModel(
            blockerLocator, configuration, sessions, activityLog, privacy, time, setupViewModel,
            new BackupViewModel(backups, sessions, RestartApp, showExport: false));
        setupViewModel.OpenAssistant = ShowSetupAssistant;

        _coldTurkey = new ColdTurkeyService(new ColdTurkeyBlockReader(), _sessions, documents);
        _coldTurkey.LoadBlocking();

        var mainViewModel = new MainWindowViewModel(
            new SessionViewModel(
                _sessions, _configuration, _endFlow, VisibleMainWindow, new UnlockViewModel(_sessions, _configuration, _unlocks), _quickSession)
            {
                TimerToggle = new TimerToggleViewModel(_sessionOverlay),
                Day = timelineDay,
                ColdTurkey = new ColdTurkeyStatusViewModel(_coldTurkey, _sessions),
            },
            new ProfilesViewModel(_configuration, _sessions, pendingChanges, overlaySettings, _coldTurkey, _link),
            new ScheduleViewModel(_configuration, _sessions, pendingChanges),
            new ExceptionsViewModel(_configuration, _sessions, calendars, _calendarSync, OpenInBrowser, pendingChanges),
            new HistoryViewModel(activityLog, _recorder, _configuration, privacy, omittedSessions, displayNames, _icons, hiddenUsage, hiddenDistractions, deletedUsage, time,
                _programPaths, desktopShell),
            setupViewModel,
            new IntegrationsViewModel(new ColdTurkeyDetector(), sightings, _configuration, _sessions),
            new SettingsViewModel(
                _configuration, _sessions, _quickSession, calendars, dataProtector, pendingChanges, dayBarSettings, overlaySettings, soundSettings, _sessionCues, sleepSettings, database.DatabasePath,
                _noise)
            {
                Backup = new BackupViewModel(backups, _sessions, RestartApp, showExport: true),
                Updates = new UpdatesViewModel(_updates),
                GoogleGuide = new GoogleSetupGuide(calendars, _calendarSync, dataProtector, documents, OpenInBrowser),
            },
            activityViewModel,
            Environment.GetEnvironmentVariable(DevToolsVariable) == "1",
            _configuration.LoadError)
        {
            DayBar = _dayBar,
        };

        mainViewModel.NoisePlayer = noisePlayer;
        _mainWindow = new MainWindow { DataContext = mainViewModel };
        _mainWindow.Title = $"AccountabiliBuddy {AccountabiliBuddy.App.Services.Backup.BackupService.AppVersion}";
        if (_usesDevData)
        {
            _mainWindow.Title += " (dev data)";
        }

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
        _icons.Start(_activityWatcher, _browserSource);
        _programPaths.Start(_activityWatcher);
        _updates.Start();
        _activityWatcher.Start();
        _browserSource.Start();
        _calendarSync.Start();
        _clockAnchors.Start();
        _sessionCues.Start();
        _sessionOverlay.Start();
        _bedtimeReminder.ReminderDue += (_, reminder) =>
            presenter.ShowReminder(reminder.Title, reminder.Message, new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#6366F1")));
        _bedtimeReminder.Start();
        _coldTurkey.ExpectedBlocksOff += (_, names) => presenter.ShowNotice("Cold Turkey block off", ColdTurkeyService.Describe(names));
        _coldTurkey.Start();
        linkHost.EndedFromDevice += (_, reason) => presenter.ShowNotice("Session ended from your phone", reason);
        _link.DevicePaired += (_, device) => presenter.ShowNotice("Device linked", $"{device.Name} is linked to this PC.");
        _ = _link.StartIfEnabledAsync();
        _activityWatcher.ActivityChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(_sessionOverlay.OnActivityChanged);
        _dayBar.Start();
        // The main window's bar redraws only while the window is open; the on-screen timeline has its own copy.
        MainWindow mainWindow = _mainWindow;
        DayBarViewModel mainDayBar = _dayBar;
        mainWindow.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.IsVisibleProperty)
            {
                if (mainWindow.IsVisible)
                {
                    mainDayBar.Resume();
                }
                else
                {
                    mainDayBar.Pause();
                }
            }
        };
        if (!mainWindow.IsVisible)
        {
            mainDayBar.Pause();
        }

        PrivateWindowAccessMonitor privateAccess = _privateAccess;
        privateAccess.NagRequested += (_, _) => ShowPrivateWindowsNag(privateAccess, browserSetup);
        privateAccess.Start();

        if (extensionUpdated)
        {
            presenter.ShowNotice(
                "Browser extension updated",
                "A newer AccountabiliBuddy extension came with this version. Reload it on each browser's extensions page (or restart the browser).");
        }

        return null;
    }

    /// <summary>
    /// Once ("history.sectionsFilled"): gives history recorded before sections were kept its subreddits and repos
    /// (ADR 17), in the background. If it fails it's tried again at the next start.
    /// </summary>
    private static async Task FillHistorySectionsOnceAsync(SqliteJsonDocumentStore documents, SqliteActivityLog log)
    {
        const string DoneKey = "history.sectionsFilled";
        try
        {
            if (await documents.LoadAsync<bool>(DoneKey))
            {
                return;
            }

            await Task.Run(() => log.FillMissingSectionsAsync());
            await documents.SaveAsync(DoneKey, true);
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
        }
    }

    /// <summary>
    /// An install or update moves the program, so registrations made by another copy (a build folder or an older
    /// install) are pointed at this one. Each step is best effort and never stops the app from starting.
    /// </summary>
    /// <returns>True if the browser extension copy was updated (the browser must reload it).</returns>
    private static bool RepairRegistrations(IStartupRegistrar startup, IQuickLaunchShortcut quickLaunch, IBrowserSetup browsers)
    {
        TryRepair(() => startup.RepairIfMoved());
        TryRepair(() =>
        {
            if (quickLaunch.IsInstalled)
            {
                quickLaunch.Install();
            }
        });
        TryRepair(() => browsers.RepairRegistrations());
        bool extensionUpdated = false;
        TryRepair(() => extensionUpdated = browsers.RefreshExtensionCopies());
        return extensionUpdated;
    }

    private static void TryRepair(Action repair)
    {
        try
        {
            repair();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException
                                       or System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            // Left for the Setup tab, which shows what needs granting again.
        }
    }

    private void ShowStartupProblem(string message)
    {
        var window = new Window
        {
            Title = "AccountabiliBuddy",
            Width = 560,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(24) },
        };
        window.Closed += (_, _) => _desktop?.Shutdown();
        window.Show();
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

    /// <summary>Shows the "allow in private windows" reminder unless it is already open; closing it snoozes it.</summary>
    private void ShowPrivateWindowsNag(PrivateWindowAccessMonitor monitor, IBrowserSetup browserSetup)
    {
        if (_privateWindowsNag is not null)
        {
            return;
        }

        var viewModel = new PrivateWindowsNagViewModel(monitor, browserSetup);
        if (viewModel.Browsers.Count == 0)
        {
            viewModel.Dispose();
            return;
        }

        _privateWindowsNag = new PrivateWindowsNagWindow { DataContext = viewModel };
        _privateWindowsNag.Closed += (_, _) =>
        {
            viewModel.Dispose();
            _privateWindowsNag = null;
            monitor.Snooze();
        };
        _privateWindowsNag.Show();
    }

    /// <summary>Starts a new copy (which waits for this one to exit) and quits, e.g. to load restored settings.</summary>
    private void RestartApp()
    {
        string? executable = Environment.ProcessPath;
        if (executable is null)
        {
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(executable, StartupOptions.RestartedArgument) { UseShellExecute = false });
        if (_mainWindow is not null)
        {
            _mainWindow.AllowClose = true;
        }

        _desktop?.Shutdown();
    }

    private void ShowSetupAssistant()
    {
        if (_assistantWindow is not null)
        {
            _assistantWindow.Activate();
            return;
        }

        if (_createAssistant is null)
        {
            return;
        }

        SetupAssistantViewModel viewModel = _createAssistant();
        _assistantWindow = new SetupAssistantWindow { DataContext = viewModel };
        _assistantWindow.Closed += (_, _) =>
        {
            viewModel.Dispose();
            _assistantWindow = null;
        };

        // Marked when opened, so a restart from inside it (restoring a backup) doesn't open it again.
        _ = _setupAssistant?.MarkShownAsync();
        _assistantWindow.Show();
        _assistantWindow.Activate();
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

        if (_noiseSettings is not null)
        {
            await _noiseSettings.FlushAsync();
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
        _sessionOverlay?.Dispose();
        _noise?.Dispose();
        _soundPlayer?.Dispose();
        _updates?.Dispose();
        _dayBar?.Dispose();
        _privateAccess?.Dispose();
        _coldTurkey?.Dispose();
        _link?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        _clockAnchors?.Dispose();
        _unlocks?.Dispose();
        _calendarSync?.Dispose();
        _calendarNetwork?.Dispose();
        _sessions?.Dispose();
        _trayIcon?.Dispose();
    }
}
