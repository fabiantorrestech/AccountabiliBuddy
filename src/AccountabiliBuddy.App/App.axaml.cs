using AccountabiliBuddy.App.DevTools;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.Services.Search;
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
    private AccountabiliBuddy.App.Services.Sync.SyncFolderService? _syncFolder;
    private AccountabiliBuddy.App.Services.Sync.QrSyncService? _qrSync;
    private NamedPipeBrowserSource? _browserSource;
    private Win32GlobalHotkey? _hotkey;
    private QuickSessionService? _quickSession;
    private StepAwayService? _stepAway;
    private PlanFirstService? _planFirst;
    private SessionReviewService? _sessionReview;
    private WorkHoursGuard? _workHours;
    private ComebackStore? _comebacks;
    private CaptureService? _capture;
    private WorkGateService? _workGate;
    private WorkGateWindow? _workGateWindow;
    private Win32GlobalHotkey? _stepAwayHotkey;
    private InstanceSignals? _instanceSignals;
    private ActivityRecorder? _recorder;
    private EnforcementCoordinator? _enforcement;
    private MainWindow? _mainWindow;
    private SetupAssistantService? _setupAssistant;
    private Func<SetupAssistantViewModel>? _createAssistant;
    private SetupAssistantWindow? _assistantWindow;
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _endSessionMenuItem;
    private NativeMenuItem? _stepAwayMenuItem;

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
        // Guided sessions (ADR 24, 25): the next or running session's checklist.
        var sessionTasks = new SessionTasksService(documents, new DpapiDataProtector(), _sessions, time);
        sessionTasks.LoadBlocking();
        _endFlow = new SessionEndFlow(_sessions);

        var startupRegistrar = new RunKeyStartupRegistrar(AppPaths.LaunchPath, StartupOptions.MinimizedArgument);

        _browserSource = new NamedPipeBrowserSource(time);
        var browserSetup = new WindowsBrowserSetup(AppPaths.NativeHostPath, AppPaths.BundledExtensionFolder, dataDirectory);
        _checkpoints = new SessionCheckpointService(_sessions, documents);
        SavedSession? savedSession = _checkpoints.LoadBlocking();
        // Loaded before the sessions start, so an unlock in effect is known at launch.
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
        _quickSession.Tasks = sessionTasks;
        _quickSession.LoadBlocking();
        // Every install has "QS - Default" (and the old "Quick Start" made by the app gets that name). After the
        // quick-session settings load: they say which profile is the default.
        _ = _quickSession.EnsureDefaultProfileAsync();
        // Step away: the countdown pauses until any input, the shortcut again or "I'm back".
        _stepAwayHotkey = new Win32GlobalHotkey();
        _stepAway = new StepAwayService(_sessions, new Win32UserIdleDetector(), _stepAwayHotkey, documents, time);
        _stepAway.LoadBlocking();
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

        var windowsEnforcer = new WindowsEnforcer();
        var presenter = new EnforcementPresenter(windowsEnforcer, _browserSource, monitorLocator, time);
        var exemptionDefaults = new ExemptionDefaultsService(documents);
        exemptionDefaults.LoadBlocking();
        presenter.PreTickedExemptionProfiles = () => exemptionDefaults.PreTicked;
        _updates = new UpdateService(
            new VelopackUpdater(), _sessions, documents, presenter.ShowNotice, () => _mainWindow?.IsVisible == true);
        _updates.LoadBlocking();
        // How far escalation may go (the computer types' "notice and overlay only", ADR 22).
        var enforcementLimit = new EnforcementLimitService(documents);
        enforcementLimit.LoadBlocking();
        var enforcementEngine = new EnforcementEngine(new RuleEvaluator(new RuleMatcher())) { MaxLevel = enforcementLimit.MaxLevel };
        enforcementLimit.Changed += (_, level) => enforcementEngine.MaxLevel = level;
        _enforcement = new EnforcementCoordinator(
            _activityWatcher,
            _browserSource,
            _sessions,
            _configuration,
            enforcementEngine,
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
        _ = FollowPlanForSleepAsync(documents, sleepSettings);
        _bedtimeReminder = new BedtimeReminderService(sleepSettings, documents, time, () => _configuration!.Current.DayPlan);
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
        // Before the day bars: their Blockers row shows Cold Turkey's blocks.
        _coldTurkey = new ColdTurkeyService(new ColdTurkeyBlockReader(), _sessions, documents);
        _coldTurkey.LoadBlocking();
        var timelineDay = new DayBarViewModel(
            _configuration, _sessions, calendars, activityLog, dayBarSettings, sleepSettings, omittedSessions, _coldTurkey, deletedUsage, time,
            ignoresShowSetting: true);
        var soundSettings = new SessionSoundSettingsService(documents);
        soundSettings.LoadBlocking();
        _soundPlayer = new Win32SoundPlayer();
        _sessionCues = new SessionCueService(_sessions, soundSettings, _soundPlayer);
        _sessionOverlay = new SessionOverlayService(
            _sessions, _configuration, overlaySettings, new Win32OverlayWindowControl(), timelineDay, _sessionCues);
        _dayBar = new DayBarViewModel(
            _configuration, _sessions, calendars, activityLog, dayBarSettings, sleepSettings, omittedSessions, _coldTurkey, deletedUsage, time);
        _noiseSettings = new NoiseSettingsService(documents);
        _noiseSettings.LoadBlocking();
        _noise = new NoisePlayerService(
            () => new AccountabiliBuddy.Platform.Windows.Audio.WindowsNoisePlayer(), _noiseSettings, () => new Win32GlobalHotkey());
        _noise.Start();
        var noisePlayer = new NoisePlayerViewModel(_noise);
        _sessionOverlay.SetNoisePlayer(noisePlayer);
        var tonightWake = new TonightWakeViewModel(sleepSettings, _configuration, time);
        tonightWake.AskToOverrideAlarm = (title, message, confirm) => presenter.ShowChoice(title, message, "Use my time", confirm, () => { });
        _sessionOverlay.SetTonightWake(tonightWake);
        _sessionOverlay.SetChecklist(new ChecklistViewModel(sessionTasks, ChecklistMode.Compact, time));
        _sessionOverlay.SetStepAway(_stepAway);
        // Plan first: a session's first 7 minutes hold unknowns (rules still enforce).
        _planFirst = new PlanFirstService(_sessions, sessionTasks);
        _enforcement.PlanFirst = _planFirst;
        // The session the stacked prompt was for ended: it goes (the review card has those unknowns).
        _sessions.StateChanged += (_, state) =>
        {
            if (!state.IsActive)
            {
                presenter.CloseStack();
            }
        };
        // Planning over with the session still running: settle what was opened meanwhile in one window.
        _planFirst.Ended += (_, _) =>
        {
            SessionState state = _sessions.State;
            if (!state.IsActive || _planFirst.HeldUnknowns.Count == 0)
            {
                return;
            }

            IReadOnlyList<FocusProfile> profiles = _configuration.Current.Profiles;
            presenter.ShowStack(
                ProfileOptions.NameOf(profiles, state.ProfileId) ?? "this session",
                state.ProfileId!.Value,
                _planFirst.HeldUnknowns.ToList(),
                profiles.Where(profile => profile.Id != state.ProfileId).Select(ProfileOption.From).ToList());
        };
        _sessionOverlay.SetPlanFirst(_planFirst);
        // Capture at the source: Ctrl+Alt+N parks an idea as a come-back (this computer, encrypted).
        _comebacks = new ComebackStore(documents, new DpapiDataProtector(), time);
        _comebacks.LoadBlocking();
        _capture = new CaptureService(_comebacks, new Win32GlobalHotkey(), monitorLocator);
        // ADR 27: work hours on this computer (off unless turned on in Settings).
        _workHours = new WorkHoursGuard(_sessions, _configuration, documents, windowsEnforcer, () => _activityWatcher.CaptureCurrent(), presenter.ShowNotice);
        _workHours.LoadBlocking();
        _activityWatcher.ActivityChanged += (_, snapshot) => Dispatcher.UIThread.Post(() => _workHours.OnActivity(snapshot));
        // The gate: the first input after 3 idle minutes during guarded work hours asks what you were about to do.
        _workGate = new WorkGateService(_workHours, new Win32UserIdleDetector(), _comebacks, _sessions);
        _workGate.Changed += (_, _) => ShowWorkGate(monitorLocator);
        // The review card after each session: what it used with no rule, as rules.
        _sessionReview = new SessionReviewService(activityLog, _configuration, documents, _sessions, () => exemptionDefaults.PreTicked);
        _sessionReview.LoadBlocking();
        // ADR 25: every task ticked: offer to end the session now (when it may), else just say so.
        sessionTasks.AllDoneReached += (_, _) =>
        {
            if (!sessionTasks.NotifyAllDone)
            {
                return;
            }

            if (sessionTasks.CanEndAtOnce)
            {
                presenter.ShowChoice("All tasks done", "Every task for this session is ticked. End the session now?", "End session",
                    () => sessionTasks.EndAtOnce(), () => { });
            }
            else
            {
                presenter.ShowNotice("All tasks done",
                    "Every task is ticked. Ending at once works when the tasks were set before the session started and it has run 10 minutes; hold Ctrl+Alt over the on-screen window for the button.");
            }
        };
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
            _sessions, _configuration, _quickSession, documents, calendars, sleepSettings, dayBarSettings, activityLog, omittedSessions, deletedUsage, privacy);
        // New activity and "Delete my data" change what paired devices were sent (ADR 15).
        _recorder.LogChanged += (_, _) => linkHost.NotifyHistoryChanged();
        _link = new AccountabiliBuddy.App.Services.Link.LinkService(
            documents, new DpapiDataProtector(), linkHost, _sessions, new AccountabiliBuddy.Platform.Windows.Network.WindowsNetworkTrust());
        _link.LoadBlocking();
        // Sharing progress between the user's own devices (ADR 22): every method but the link is off until turned on.
        var syncSettings = new AccountabiliBuddy.App.Services.Sync.SyncSettingsService(documents);
        syncSettings.LoadBlocking();
        var receivedDevices = new AccountabiliBuddy.App.Services.Sync.ReceivedDevicesService(documents, time);
        receivedDevices.LoadBlocking();
        // A paired phone's next alarm (ADR 23): the newest phone file's, while it's still ahead.
        void FollowPhoneAlarm() => sleepSettings.SetPhoneAlarm(AccountabiliBuddy.App.Services.Sync.PhoneAlarm.Of(receivedDevices.Devices, time));
        receivedDevices.Changed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(FollowPhoneAlarm);
        FollowPhoneAlarm();
        var syncIdentity = new AccountabiliBuddy.App.Services.Sync.SyncIdentityService(documents, dataProtector, receivedDevices);
        syncIdentity.LoadBlocking();
        var syncEditTimes = new AccountabiliBuddy.App.Services.Sync.SyncEditTimesService(_configuration, sleepSettings, dayBarSettings, documents, time);
        syncEditTimes.LoadBlocking();
        var syncCoordinator = new AccountabiliBuddy.App.Services.Sync.SyncCoordinator(
            _sessions, _configuration, activityLog, omittedSessions, deletedUsage, privacy, sleepSettings, dayBarSettings, documents, syncSettings,
            syncIdentity, receivedDevices, syncEditTimes);
        // Guided sessions (ADR 24): the checklist goes to paired devices in the link's state and in this PC's sync file.
        linkHost.Tasks = () => sessionTasks.IsLive ? sessionTasks.Current.Tasks : null;
        linkHost.CurrentTask = () => sessionTasks.IsLive ? sessionTasks.Current.CurrentId : null;
        syncCoordinator.TasksOf = start => sessionTasks.ForSession(start)?.Tasks is { Count: > 0 } tasks ? tasks : null;
        sessionTasks.Changed += (_, _) =>
        {
            linkHost.NotifyTasksChanged();
            syncCoordinator.NotifyHistoryChanged();
        };
        syncCoordinator.LoadBlocking();
        linkHost.UseSync(syncCoordinator, receivedDevices, syncIdentity, syncSettings);
        // The running log (ADR 22): the other devices' sessions on both day bars (History gets them below).
        _dayBar.UseReceived(receivedDevices);
        _dayBar.UseTasks(sessionTasks);
        timelineDay.UseTasks(sessionTasks);
        timelineDay.UseReceived(receivedDevices);
        _syncFolder = new AccountabiliBuddy.App.Services.Sync.SyncFolderService(syncCoordinator, syncSettings, syncIdentity, receivedDevices, _sessions);

        var setupViewModel = new SetupViewModel(
            startupRegistrar,
            quickLaunch,
            browserSetup,
            _browserSource,
            new PrivacySetupViewModel(privacy, _recorder, ocrReader.IsAvailable),
            _sessions,
            _watchedBrowsers,
            _privateAccess);
        _setupAssistant = new SetupAssistantService(documents, time);
        _setupAssistant.LoadBlocking();
        var blockerLocator = new WindowsBlockerSourceLocator();
        ConfigurationService configuration = _configuration;
        SessionController sessions = _sessions;
        var backups = new BackupService(_configuration, documents, activityLog, new DpapiDataProtector(), time, dataDirectory);
        // The computer types (ADR 22): the assistant applies one; Settings shows it and can apply one again.
        var computerTypes = new AccountabiliBuddy.App.Services.Setup.ComputerTypeService(documents, privacy, enforcementLimit, syncSettings, _updates);
        computerTypes.LoadBlocking();
        _createAssistant = () => new SetupAssistantViewModel(
            blockerLocator, configuration, sessions, activityLog, privacy, time, setupViewModel,
            new BackupViewModel(backups, sessions, RestartApp, showExport: false))
        {
            Types = computerTypes,
            Identity = syncIdentity,
        };
        setupViewModel.OpenAssistant = ShowSetupAssistant;

        // Search reads the user's things when asked: History's apps and sites (last 90 days, re-read at most once a minute),
        // the calendar's events around today, profiles and the day plan.
        IReadOnlyList<AppSiteUsage> searchUsage = Array.Empty<AppSiteUsage>();
        DateTimeOffset searchUsageReadAt = DateTimeOffset.MinValue;
        async Task<IReadOnlyList<SearchEntry>> SearchDataAsync()
        {
            DateTimeOffset now = time.GetUtcNow();
            if (now - searchUsageReadAt > TimeSpan.FromMinutes(1))
            {
                try
                {
                    // Off the UI thread: Microsoft.Data.Sqlite's async calls run synchronously, and 90 days is a big read.
                    IReadOnlyList<UsageDeletion> deletions = deletedUsage.Parts.ToList();
                    searchUsage = (await Task.Run(() => activityLog.GetSummaryAsync(now.AddDays(-90), now, null, deletions))).AppsAndSites;
                    searchUsageReadAt = now;
                }
                catch (Microsoft.Data.Sqlite.SqliteException)
                {
                    // Keep the last list; the next search tries again.
                }
            }

            DateOnly today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
            var events = new List<CalendarEvent>();
            var seenEvents = new HashSet<(string, string)>();
            for (int day = -60; day <= 120; day++)
            {
                foreach (CalendarEntry entry in calendars.EventsOn(today.AddDays(day)))
                {
                    if (seenEvents.Add((entry.Event.CalendarId, entry.Event.Id)))
                    {
                        events.Add(entry.Event);
                    }
                }
            }

            var shownNames = searchUsage.Select(usage => usage.Site ?? usage.App).Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(name => name, displayNames.NameFor, StringComparer.OrdinalIgnoreCase);
            return DataSearchSource.Entries(_configuration!.Current, events, searchUsage, shownNames);
        }

        // The Devices tab (ADR 22): the group, the sharing methods and the phones paired over the link (from Setup).
        var devices = new DevicesViewModel(syncIdentity, syncSettings, receivedDevices, syncCoordinator, _sessions, time)
        {
            LinkedDevices = new LinkedDevicesViewModel(_link, _sessions),
        };
        var sessionViewModel = new SessionViewModel(
            _sessions, _configuration, _endFlow, VisibleMainWindow, new UnlockViewModel(_sessions, _configuration, _unlocks), _quickSession)
        {
            TimerToggle = new TimerToggleViewModel(_sessionOverlay),
            Day = timelineDay,
            ColdTurkey = new ColdTurkeyStatusViewModel(_coldTurkey, _sessions),
        };
        // Focus mode: the Today page while a session runs (the main window's day bar is the one it reads).
        sessionViewModel.Comebacks = new ComebacksViewModel(_comebacks!);
        sessionViewModel.Review = _sessionReview!.Pending;
        _sessionReview.Changed += (_, _) => sessionViewModel.Review = _sessionReview.Pending;
        sessionViewModel.Focus = new FocusViewModel(
            _sessions, _configuration, _stepAway, sessionViewModel.Tasks, _dayBar, () => _endFlow.TryEndSessionAsync(VisibleMainWindow()))
        {
            PlanFirst = _planFirst,
        };
        var mainViewModel = new MainWindowViewModel(
            sessionViewModel,
            new ProfilesViewModel(_configuration, _sessions, pendingChanges, overlaySettings, _coldTurkey, _link, exemptionDefaults),
            new DayPlanViewModel(_configuration, _sessions),
            new ExceptionsViewModel(_configuration, _sessions, calendars, _calendarSync, OpenInBrowser, pendingChanges),
            new HistoryViewModel(activityLog, _recorder, _configuration, privacy, omittedSessions, displayNames, _icons, hiddenUsage, hiddenDistractions, deletedUsage, time,
                _programPaths, desktopShell, isSessionRunning: () => _sessions!.State.IsActive)
            {
                ReviewService = _sessionReview,
            },
            setupViewModel,
            new IntegrationsViewModel(new ColdTurkeyDetector(), sightings, _configuration, _sessions),
            new SettingsViewModel(
                _configuration, _sessions, _quickSession, calendars, dataProtector, pendingChanges, dayBarSettings, overlaySettings, soundSettings, _sessionCues, sleepSettings, database.DatabasePath,
                _noise)
            {
                StepAway = _stepAway,
                WorkHours = new WorkHoursViewModel(_workHours!, _sessions),
                Backup = new BackupViewModel(backups, _sessions, RestartApp, showExport: true),
                Updates = new UpdatesViewModel(_updates),
                EnforcementLimit = enforcementLimit,
                ComputerTypes = computerTypes,
                PrivacyChoices = privacy,
                GoogleGuide = new GoogleSetupGuide(calendars, _calendarSync, dataProtector, documents, OpenInBrowser),
            },
            activityViewModel,
            Environment.GetEnvironmentVariable(DevToolsVariable) == "1",
            _configuration.LoadError)
        {
            DayBar = _dayBar,
            Search = new SearchViewModel(SearchDataAsync),
            Devices = devices,
        };

        // A session added, edited or deleted by hand (ADR 20): both day bars read the history again, and linked devices get it.
        DayBarViewModel addedSessionsBar = _dayBar;
        mainViewModel.History.AddedSessionsChanged += (_, _) =>
        {
            addedSessionsBar.ReloadHistory();
            timelineDay.ReloadHistory();
            linkHost.NotifyHistoryChanged();
            syncCoordinator.NotifyHistoryChanged();
        };
        mainViewModel.History.UseReceived(receivedDevices);

        // The QR code method (ADR 22): today's codes from a button (top bar, Ctrl+Alt details) and an end-of-day reminder.
        var qrSync = new AccountabiliBuddy.App.Services.Sync.QrSyncService(
            syncCoordinator, syncIdentity, syncSettings, receivedDevices, _configuration, documents, time);
        qrSync.LoadBlocking();
        _qrSync = qrSync;
        var syncImport = new AccountabiliBuddy.App.Services.Sync.SyncImportService(syncCoordinator, syncIdentity);
        void ShowSyncQr() => new SyncQrWindow { DataContext = new SyncQrViewModel(qrSync.LinksForNowAsync) }.Show();
        void ShowQrButtons(AccountabiliBuddy.App.Services.Sync.SyncSettings settings)
        {
            mainViewModel.ShowsQrButton = settings.QrOn;
            _sessionOverlay!.SetSyncQr(settings.QrOn ? ShowSyncQr : null);
        }

        mainViewModel.ShowSyncQr = ShowSyncQr;
        devices.ShowSyncQr = ShowSyncQr;
        devices.Import = syncImport;
        devices.PairedDevices = () => _link.Devices;
        devices.UnpairDevice = _link.UnpairAsync;
        devices.SyncNow = async () =>
        {
            await _syncFolder!.WriteNowAsync();
            // Seals this PC's file again for the link, which hands it to connected devices.
            syncCoordinator.NotifyHistoryChanged();
        };
        ShowQrButtons(syncSettings.Current);
        syncSettings.Changed += (_, settings) => ShowQrButtons(settings);
        qrSync.ReminderDue += (_, _) => presenter.ShowWithLater(
            "Scan today's code to sync your phone",
            "Your phone and watch get today's focus from this PC's QR codes.",
            "Show code",
            ShowSyncQr,
            minutes => _ = qrSync.SnoozeAsync(minutes));

        mainViewModel.NoisePlayer = noisePlayer;
        mainViewModel.Settings.OpenDayPlan = () => mainViewModel.SelectedTab = MainWindowViewModel.DayPlanTab;
        _mainWindow = new MainWindow { DataContext = mainViewModel };
        // Sync files and pictures of the phone's codes dropped on the window (ADR 22), when their method is on.
        _mainWindow.FilesDropped += async (_, paths) =>
            presenter.ShowNotice("Sync", await ImportDroppedAsync(syncImport, syncSettings.Current, paths));
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
        _ = sessionTasks.AfterStartupAsync();
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
        // Ctrl+Alt + double-click on the on-screen window, or its Open button.
        _sessionOverlay.OpenAppRequested += (_, _) => ShowMainWindow();
        _sessionOverlay.Start();
        _bedtimeReminder.ReminderDue += (_, reminder) =>
            presenter.ShowReminder(reminder.Title, reminder.Message, new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#6366F1")));
        _bedtimeReminder.Start();
        _coldTurkey.ExpectedBlocksOff += (_, names) => presenter.ShowNotice("Cold Turkey block off", ColdTurkeyService.Describe(names));
        _coldTurkey.Start();
        linkHost.EndedFromDevice += (_, reason) => presenter.ShowNotice("Session ended from your phone", reason);
        _link.DevicePaired += (_, device) => presenter.ShowNotice("Device linked", $"{device.Name} is linked to this PC.");
        // Once per network: "Phone link paused" on a network Windows marks Public, with a way to Windows' setting.
        var publicNotices = new AccountabiliBuddy.App.Services.Link.PublicNetworkNoticeService(documents);
        publicNotices.LoadBlocking();
        _link.PausedOnPublicNetwork += async (_, network) =>
        {
            if (await publicNotices.ShouldTellAsync(network.Name, _link.Devices.Count > 0))
            {
                presenter.ShowChoice(
                    $"Phone link paused on {network.Name}",
                    $"Windows marks {network.Name} as Public, so AccountabiliBuddy doesn't listen for your phone here. If it's your own "
                    + $"network, set it to Private: Settings → Network & internet → {(network.IsWireless ? "Wi-Fi" : "Ethernet")} → "
                    + $"{network.Name} → Private network. Your phone reconnects by itself, no new pairing.",
                    "Open network settings",
                    () => OpenInBrowser(new Uri(network.IsWireless ? "ms-settings:network-wifi" : "ms-settings:network-ethernet")),
                    () => { });
            }
        };
        _ = _link.StartIfEnabledAsync();
        syncCoordinator.ClashRaised += (_, request) => ShowDayPlanClash(syncCoordinator, request, presenter, time);
        _syncFolder.Start();
        _qrSync?.Start();
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
    /// Sleep mode's times come from the day plan's Sleep parts (ADR 18). Once ("dayplan.sleepSeeded"), the plan takes
    /// the sleep times set before the day plan existed; from then on the sleep settings follow the plan.
    /// </summary>
    private async Task FollowPlanForSleepAsync(SqliteJsonDocumentStore documents, SleepSettingsService sleep)
    {
        const string SeededKey = "dayplan.sleepSeeded";
        ConfigurationService configuration = _configuration!;
        try
        {
            if (!await documents.LoadAsync<bool>(SeededKey))
            {
                SleepSettings before = sleep.Current;
                DayPlan seeded = DayPlanSleep.WithSleep(
                    configuration.Current.DayPlan, before.Weeknights, before.SameEveryNight ? before.Weeknights : before.Weekends);
                await configuration.UpdateAsync(document => document with { DayPlan = seeded });
                await documents.SaveAsync(SeededKey, true);
            }

            await sleep.SaveFromPlanAsync(configuration.Current.DayPlan);
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            // Tried again at the next start; the sleep settings keep their last times meanwhile.
        }

        configuration.Changed += (_, document) => _ = sleep.SaveFromPlanAsync(document.DayPlan);
    }

    /// <summary>
    /// Once ("history.sectionsFilled"): gives history recorded before sections were kept its subreddits and repos
    /// (ADR 17), in the background. If it fails it's tried again at the next start.
    /// </summary>
    /// <summary>
    /// Sync files (*.absync, with the sync file method on) and pictures of the phone's QR codes (with the QR code
    /// method on) dropped on the main window; returns what happened, e.g. "Synced Pixel 7".
    /// </summary>
    private static async Task<string> ImportDroppedAsync(
        AccountabiliBuddy.App.Services.Sync.SyncImportService import, AccountabiliBuddy.App.Services.Sync.SyncSettings settings, IReadOnlyList<string> paths)
    {
        List<string> files = paths
            .Where(path => path.EndsWith(AccountabiliBuddy.App.Services.Sync.SyncFolderService.Extension, StringComparison.OrdinalIgnoreCase))
            .ToList();
        List<string> pictures = paths.Except(files).ToList();
        var results = new List<string>();
        if (files.Count > 0)
        {
            if (!settings.FileShareOn)
            {
                results.Add("Turn on \"Sync file\" in Devices to take in sync files.");
            }
            else
            {
                foreach (string file in files)
                {
                    results.Add(await import.ImportFileAsync(file));
                }
            }
        }

        if (pictures.Count > 0)
        {
            results.Add(settings.QrOn
                ? await import.ImportPicturesAsync(pictures)
                : "Turn on \"QR codes\" in Devices to read your phone's codes from a picture.");
        }

        return string.Join(" · ", results.Distinct());
    }

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

        // Allowed during focus (Settings isn't): only redraws what would show anyway.
        var onScreenItem = new NativeMenuItem("Show on-screen window again");
        onScreenItem.Click += (_, _) => _sessionOverlay?.ShowAgain();

        // Allowed during focus: it pauses the countdown, it never shortens a wait.
        _stepAwayMenuItem = new NativeMenuItem("Step away");
        _stepAwayMenuItem.Click += (_, _) => _stepAway?.Toggle();

        _endSessionMenuItem = new NativeMenuItem("End session…");
        _endSessionMenuItem.Click += async (_, _) => await _endFlow!.TryEndSessionAsync(VisibleMainWindow());

        var quitItem = new NativeMenuItem("Quit");
        quitItem.Click += async (_, _) => await QuitAsync();

        var menu = new NativeMenu();
        menu.Items.Add(quickItem);
        menu.Items.Add(openItem);
        menu.Items.Add(onScreenItem);
        menu.Items.Add(_stepAwayMenuItem);
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

    /// <summary>Shows or closes the work-hours gate (ADR 27) as the service opens or closes it.</summary>
    private void ShowWorkGate(IMonitorLocator monitors)
    {
        WorkGateViewModel? gate = _workGate?.Current;
        if (gate is null)
        {
            WorkGateWindow? open = _workGateWindow;
            _workGateWindow = null;
            open?.Close();
            return;
        }

        if (_workGateWindow is not null)
        {
            return;
        }

        var window = new WorkGateWindow { DataContext = gate };
        window.Closed += (_, _) =>
        {
            if (_workGateWindow == window)
            {
                // Closed some other way: asked again at the next check.
                _workGateWindow = null;
                _workGate?.WindowClosed();
            }
        };
        if (monitors.Find(monitors.FindMonitorUnderCursor()) is MonitorArea area)
        {
            MonitorPlacement.Center(window, area);
        }

        _workGateWindow = window;
        window.Show();
        window.Activate();
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

        if (_stepAwayMenuItem is not null && _stepAway is not null)
        {
            string shortcut = _stepAway.Settings.Hotkey.Length > 0 ? $"  ({_stepAway.Settings.Hotkey})" : string.Empty;
            string header = state.IsAway ? $"I'm back{shortcut}" : $"Step away{shortcut}";
            if (_stepAwayMenuItem.Header != header)
            {
                _stepAwayMenuItem.Header = header;
            }

            _stepAwayMenuItem.IsEnabled = state.IsActive;
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

    /// <summary>
    /// The day plan changed here and on another device (ADR 22): the dialog over the main window when it's open, else a
    /// notice that opens it. Closing the dialog or dismissing the notice uses the latest edit.
    /// </summary>
    private void ShowDayPlanClash(
        AccountabiliBuddy.App.Services.Sync.SyncCoordinator coordinator,
        AccountabiliBuddy.App.Services.Sync.DayPlanClashRequest request,
        EnforcementPresenter presenter,
        TimeProvider time)
    {
        if (_mainWindow is { IsVisible: true })
        {
            ShowDayPlanClashDialog(coordinator, request, time);
            return;
        }

        presenter.ShowChoice(
            "Day plan changed on two devices",
            $"It was changed here and on {request.FromDevice}. Choose which to keep, or the latest edit is used.",
            "Choose…",
            () =>
            {
                ShowMainWindow();
                ShowDayPlanClashDialog(coordinator, request, time);
            },
            () => _ = coordinator.ResolveClashAsync(request, AccountabiliBuddy.App.Services.Sync.DayPlanClashChoice.UseLatest));
    }

    private void ShowDayPlanClashDialog(
        AccountabiliBuddy.App.Services.Sync.SyncCoordinator coordinator, AccountabiliBuddy.App.Services.Sync.DayPlanClashRequest request, TimeProvider time)
    {
        var viewModel = new DayPlanClashViewModel(request, time, choice => _ = coordinator.ResolveClashAsync(request, choice));
        var dialog = new DayPlanClashDialog { DataContext = viewModel };
        if (_mainWindow is { IsVisible: true })
        {
            dialog.Show(_mainWindow);
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.Show();
        }

        dialog.Activate();
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

        // Focus mode: while a session runs the window opens on Today, whatever was showing before.
        if (_mainWindow.DataContext is MainWindowViewModel { IsFocusMode: true } focusMode)
        {
            focusMode.SelectedTab = (int)MainTab.Today;
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
        _stepAway?.Dispose();
        _planFirst?.Dispose();
        _sessionReview?.Dispose();
        _workHours?.Dispose();
        _capture?.Dispose();
        _workGate?.Dispose();
        _stepAwayHotkey?.Dispose();
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
        _syncFolder?.Dispose();
        _qrSync?.Dispose();
        _clockAnchors?.Dispose();
        _unlocks?.Dispose();
        _calendarSync?.Dispose();
        _calendarNetwork?.Dispose();
        _sessions?.Dispose();
        _trayIcon?.Dispose();
    }
}
