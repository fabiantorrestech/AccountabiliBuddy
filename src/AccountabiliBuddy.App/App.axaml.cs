using AccountabiliBuddy.App.DevTools;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.Services.Search;
using AccountabiliBuddy.App.Services.Theming;
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
using AccountabiliBuddy.Platform.Windows.Location;
using AccountabiliBuddy.Platform.Windows.Network;
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
    private NetworkTimeService? _networkTime;
    private AccountabiliBuddy.App.Services.Backup.AutoBackupService? _autoBackup;
    private bool _appUpdated;
    private ThemeService? _themes;
    private AppTheme? _appearance;
    private WatchedBrowsersService? _watchedBrowsers;
    private UnlockService? _unlocks;
    private ArmedEndService? _armedEnds;
    private RunningWaitsService? _runningWaits;
    private SessionOverlayService? _sessionOverlay;
    private AccountabiliBuddy.Platform.Windows.Taskbar.Win32TaskbarGeometry? _taskbarGeometry;
    private AccountabiliBuddy.Platform.Windows.Power.WinRtBatteryReader? _batteryReader;
    private AccountabiliBuddy.Platform.Windows.Hotkeys.Win32CtrlAltWatcher? _ctrlAltWatcher;
    private AccountabiliBuddy.Platform.Windows.Hotkeys.Win32PanelKeys? _panelKeys;
    private Win32GlobalHotkey? _overlayStickyHotkey;
    private Win32GlobalHotkey? _overlayHideHotkey;
    private PcAlarmService? _pcAlarm;
    private AccountabiliBuddy.App.Services.Timers.TimersService? _timers;
    private NoisePlayerService? _noise;
    private NoiseSettingsService? _noiseSettings;
    private HeadphoneAutoPlayService? _headphones;
    private LightsOutService? _lightsOut;
    private AccountabiliBuddy.App.Services.Setup.ComputerTypeService? _computerTypes;
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
    private ColdTurkeyKeysService? _coldTurkeyKeys;
    private QuitGuardService? _quitGuard;
    private static readonly TimeSpan SessionHeartbeat = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan GoingOffWriteBudget = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan GoingOffStale = TimeSpan.FromSeconds(30);
    private PcOffSettingsService? _pcOff;
    private AccountabiliBuddy.App.Services.Tasks.TaskSettingsService? _taskSettings;
    private AccountabiliBuddy.App.Services.TaskFilterPresetStore? _filterPresets;
    private AccountabiliBuddy.App.Services.Tasks.TaskActionsService? _taskActions;

    // Which layout Tasks shows around a session ("Ended · switches next time"); read by the title bar's session control.
    private AccountabiliBuddy.App.Services.Tasks.SessionLayoutService? _sessionLayout;

    // Round 2: how the PC's Today page is arranged ("today.layout", device-local; Settings › Today).
    private AccountabiliBuddy.App.Services.Today.TodayLayoutService? _todayLayout;
    private StepsMovedCardViewModel? _stepsMovedCard;
    private AccountabiliBuddy.App.Services.Tasks.TaskViewService? _taskViews;
    private AccountabiliBuddy.App.Services.Tasks.TaskTidyUpService? _taskTidyUp;

    // Round 2 R4: this PC's goals (store, Deep work rules); the Goals screens come in R5.
    private AccountabiliBuddy.App.Services.Goals.GoalsService? _goals;
    private AccountabiliBuddy.App.Services.Tasks.TaskReminderService? _taskReminders;
    private AccountabiliBuddy.App.Services.Tasks.TaskReminderRinger? _taskReminderRinger;

    // Round 2 R5: goal reminders (through the task reminder engine) and the weekly goals review.
    private AccountabiliBuddy.App.Services.Goals.GoalReminderSource? _goalReminders;
    private AccountabiliBuddy.App.Services.Goals.GoalReviewService? _goalReview;
    private AccountabiliBuddy.App.Services.Tasks.TaskCalendarExportService? _taskCalendarExport;
    private AccountabiliBuddy.App.Services.Tasks.TaskReminderSettingsService? _taskReminderSettings;
    private AccountabiliBuddy.App.Services.Tasks.TaskAccountSettingsService? _taskAccount;
    // Settings › Alerts & on-screen › Task reminders (ring on); the setup assistant's "Which devices should ring?" page shows its switches.
    private TaskReminderAlertsViewModel? _taskReminderAlerts;
    private AccountabiliBuddy.App.Services.Sync.SyncCoordinator? _syncCoordinator;
    private bool _windowsEndingHandled;

    // Avalonia 12 keeps ShutdownRequestedEventArgs.IsOSShutdown internal, so it is read by reflection; false if it ever goes away.
    private static bool IsOsShutdown(ShutdownRequestedEventArgs e)
    {
        System.Reflection.PropertyInfo? property = typeof(ShutdownRequestedEventArgs).GetProperty("IsOSShutdown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        return property?.GetValue(e) is true;
    }

    /// <summary>True once Windows asked the app to shut down; Program.Main then checks whether the shutdown really happened.</summary>
    public static bool WindowsShutdownRequested { get; private set; }
    private DateTimeOffset _lastSessionWrite = DateTimeOffset.MinValue;
    private QuitGuardViewModel? _quitGuardViewModel;
    private AccountabiliBuddy.App.Services.Setup.SetupFeaturesService? _setupFeatures;
    private EnforcementPresenter? _presenter;
    private Window? _coldTurkeyKeyWindow;
    private AccountabiliBuddy.App.Services.Sync.SyncFolderService? _syncFolder;
    private AccountabiliBuddy.App.Services.Sync.SyncDriveService? _syncDrive;
    private AccountabiliBuddy.App.Services.Sync.SyncRelayService? _syncRelay;
    private AccountabiliBuddy.App.Services.Sync.SyncRemovalService? _syncRemoval;
    private UnlinkedViewModel? _unlinkedViewModel;

    // Set when "Start over" was asked for and could not clear the data at start; told to the user once the app is up.
    private string? _startOverProblem;
    private UnlinkedDialog? _unlinkedDialog;

    // The unlinked state the dialog was last shown for, so closing it without choosing doesn't bring it back until the next start.
    private DateTimeOffset? _unlinkedShownFor;
    private AccountabiliBuddy.App.Services.Sync.RelayHostService? _relayHost;
    private AccountabiliBuddy.App.Services.Sync.QrSyncService? _qrSync;
    private NamedPipeBrowserSource? _browserSource;
    private Win32GlobalHotkey? _hotkey;
    private QuickSessionService? _quickSession;
    private StepAwayService? _stepAway;
    private PlanFirstService? _planFirst;
    private SessionReviewService? _sessionReview;
    private WorkHoursGuard? _workHours;
    private HyperfocusGuard? _hyperfocus;
    private StartNudgeService? _startNudge;
    private IWindowAttention? _windowAttention;
    private LunchService? _lunch;
    private LunchRunIntoService? _lunchRunInto;
    private LunchRunIntoCardWindow? _lunchRunIntoWindow;
    private Motion? _motion;
    private GoalCardViewModel? _goalCard;
    private WeekCardViewModel? _weekCard;
    private AccountabiliBuddy.App.Services.Sync.PhonePickups? _phonePickups;
    private AccountabiliBuddy.App.Services.Sync.PhonePlacementService? _phonePlacement;
    private DeepWorkService? _deepWork;
    private ShutdownService? _shutdown;
    private WorkEndedService? _workEnded;
    private GuidanceModeService? _guidance;
    private GuidanceModeViewModel? _guidanceViewModel;
    private ModeSettings? _modeSettings;
    private EndWorkEarlyDialog? _endWorkDialog;
    private ComebackStore? _comebacks;
    private LaterStore? _later;
    // ADR 72: every task list syncs (history, settle records, Auto-saved, local items).
    private AccountabiliBuddy.App.Services.Sync.TaskListSync? _taskListSync;
    private ReceivedTaskHistoryStore? _receivedHistory;
    private KeptPastStore? _keptPast;
    private PastSettledStore? _pastSettled;
    private AccountabiliBuddy.App.Services.Beeminder.BeeminderService? _beeminder;
    private AccountabiliBuddy.App.Services.Beeminder.BeeminderDatapointsService? _beeminderDatapoints;
    private SessionNotesStore? _sessionNotes;
    private CaptureService? _capture;
    private WorkGateService? _workGate;
    private WorkGateWindow? _workGateWindow;
    private StartNudgeCardWindow? _startNudgeCardWindow;
    private StartOfDayWindow? _startOfDayWindow;
    // The cards in front of everything (gate, Pre-Work, start nudge, session cards): dim behind, kept in front, follow the virtual desktop.
    private readonly FrontCardHost _frontCards = new(new WindowsVirtualDesktops(), new WindowsForegroundFocus(), new Win32OverlayWindowControl());
    private PowerTransitions? _power;
    private AwayCardWindow? _awayCardWindow;
    private PickTasksWindow? _pickTasksWindow;
    private ReadyCardWindow? _readyCardWindow;
    private PreWorkService? _preWork;
    private PreWorkWindow? _preWorkWindow;
    private Win32GlobalHotkey? _stepAwayHotkey;
    private InstanceSignals? _instanceSignals;
    private ActivityRecorder? _recorder;
    private EnforcementCoordinator? _enforcement;
    private MainWindow? _mainWindow;
    private SetupAssistantService? _setupAssistant;
    private Func<SetupAssistantViewModel>? _createAssistant;
    private SetupAssistantWindow? _assistantWindow;
    private Action? _showDpAssistant;
    private Action? _showCalendarRules;
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
        // The look (ADR 49): the theme tokens must be in place before any window exists. Instrument until the Appearance settings
        // choose another; light or dark stays with AppTheme.
        _themes = new ThemeService();
        _themes.Apply(ThemeName.Instrument);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            // The app lives in the tray; only Quit ends it.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) => DisposeServices();
            desktop.ShutdownRequested += (_, e) =>
            {
                WindowsShutdownRequested |= IsOsShutdown(e);
                OnWindowsEnding();
            };

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
                    ShowSetupAssistant(quick: true);
                }
            }

            // Removed from the synced devices while this PC was off or away (ADR 54): ask what to do now.
            ShowUnlinkedIfNew();
            if (_startOverProblem is not null)
            {
                _presenter?.ShowNotice("Start over didn't happen", _startOverProblem);
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
        // "Start over" asked for the data to be cleared (ADR 54, 10): done now, before the database is opened.
        AccountabiliBuddy.App.Services.Setup.StartOverService.RunPending(dataDirectory, out _startOverProblem);
        var database = new LocalDatabase(dataDirectory);
        string? problem = UpgradeSafety.CheckBlocking(database);
        if (problem is not null)
        {
            return problem;
        }

        var documents = new SqliteJsonDocumentStore(database, time);
        // First launch of a new version: keep a copy of the data before anything migrates it.
        // A database copy when this version runs for the first time; then auto-backup writes every kind too (ADR 71).
        _appUpdated = UpgradeSafety.BackupIfNewVersionBlocking(database, documents, BackupService.AppVersion, time) is not null;
        _clockAnchors = new ClockAnchorService(time, documents);
        _clockAnchors.RestoreBlocking();
        _configuration = new ConfigurationService(new SqliteProfileStore(database, time), documents, time);
        _configuration.LoadBlocking();

        _sessions = new SessionController(new SessionEngine(time), _configuration, time);
        var calendars = new CalendarService(documents);
        calendars.LoadBlocking();
        // ADR 58: calendar events laid over the day plan, in memory, for every guard; the plan editors and sync keep the stored plan.
        var calendarOverlay = new CalendarOverlayStore(documents);
        calendarOverlay.LoadBlocking();
        var effectivePlan = new EffectiveDayPlanProvider(_configuration, calendars, calendarOverlay, _sessions, time);
        effectivePlan.Start();
        // Guided sessions (ADR 24, 25): the next or running session's checklist.
        var sessionTasks = new SessionTasksService(documents, new DpapiDataProtector(), _sessions, time);
        sessionTasks.LoadBlocking();
        // ADR 76: the one task list; it becomes the source once every list is loaded (TaskModelMigration, below).
        var taskStore = new AccountabiliBuddy.App.Services.Tasks.TaskStore(documents, new DpapiDataProtector(), time);
        // The account's Tasks settings (default view, picked calendar): kept and synced with the task list, the same on every device.
        var taskAccount = new AccountabiliBuddy.App.Services.Tasks.TaskAccountSettingsService(taskStore);
        _taskAccount = taskAccount;
        // ADR 76: Settings › Tasks (actions per session, the next step takes the slot); more actions is a looser change and waits.
        var taskSettings = new AccountabiliBuddy.App.Services.Tasks.TaskSettingsService(
            documents, time, _sessions, () => _configuration!.Current.Locks.LooseningDelay);
        taskSettings.LoadBlocking();
        sessionTasks.Settings = taskSettings;
        // The list's This session bin is the next session's draft: split by the setting (the next session starts with it). The running
        // checklist keeps its own cap (SessionTasksService.ActionCap), which its add, remote adds and undo's offer go through.
        taskStore.SessionActions = () => taskSettings.SessionActions;
        taskStore.ReviewPending = () => _sessionReview?.Pending is not null;
        _taskSettings = taskSettings;
        // ADR 77: each session keeps the step-away setting it started with (in its checkpoint too).
        _sessions.StepAwaySetting = () => taskSettings.Current.StepAway;
        _sessions.SideTaskSetting = () => taskSettings.Current.SideTask;
        // ADR 66: waited ends confirmed early apply themselves when their wait is over; the notice shows where the cursor is.
        _armedEnds = new ArmedEndService(_sessions, documents, new DpapiDataProtector(), (title, message) => _presenter?.ShowNotice(title, message));
        var sessionArmedEnd = new SessionArmedEndHandler(_sessions);
        _armedEnds.Register(sessionArmedEnd);
        // A session that ended some other way (completed, ended from the phone, unlocked) takes its armed end with it.
        _sessions.SessionEnded += (_, _) => _armedEnds.Drop(ArmedEndKind.EndSession);
        _endFlow = new SessionEndFlow(_sessions, _armedEnds);
        // Every wait running in the background, for the tile, Today and the tray (ADR 66).
        _runningWaits = new RunningWaitsService(_sessions);
        ArmedEndService armedEnds = _armedEnds;
        _runningWaits.AddSource(() => armedEnds.Current.Select(end => new RunningWait(
            $"armed:{end.Id}", armedEnds.Label(end), end.ReadyAt, () => { armedEnds.Cancel(end.Id); return Task.CompletedTask; })));
        armedEnds.Changed += (_, _) => _runningWaits.NotifyChanged();

        var startupRegistrar = new RunKeyStartupRegistrar(AppPaths.LaunchPath, StartupOptions.MinimizedArgument);

        _browserSource = new NamedPipeBrowserSource(time);
        var browserSetup = new WindowsBrowserSetup(AppPaths.NativeHostPath, AppPaths.BundledExtensionFolder, dataDirectory);
        _checkpoints = new SessionCheckpointService(_sessions, documents, new RegistryBootIdProvider().GetBootId());
        SavedSession? savedSession = _checkpoints.LoadBlocking();
        // Loaded before the sessions start, so an unlock in effect is known at launch.
        _unlocks = new UnlockService(_sessions, documents);
        _unlocks.LoadBlocking();
        UnlockService unlocks = _unlocks;
        _runningWaits.AddSource(() => unlocks.Current is { } unlock && unlocks.Status == UnlockStatus.Waiting
            ? [new RunningWait("unlock", $"Unlock ({unlock.OptionName})", unlock.ReadyAt, () => { unlocks.Cancel(); return Task.CompletedTask; })]
            : []);
        unlocks.Changed += (_, _) => _runningWaits.NotifyChanged();
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
        // Step away: the countdown pauses until "Back on track" (the card the first input brings up), the shortcut again or "I'm back".
        _stepAwayHotkey = new Win32GlobalHotkey();
        _stepAway = new StepAwayService(_sessions, new Win32UserIdleDetector(), new Win32CursorPosition(), _stepAwayHotkey, documents, time);
        // ADR 77: "When I step away" (keep the timer running by default), never looser than at the running session's start.
        _stepAway.StepAwaySetting = () => _taskSettings?.StepAwayNow ?? AccountabiliBuddy.Core.Engine.StepAwayTimerPolicy.Default;
        _stepAway.SideTaskSetting = () => _taskSettings?.SideTaskNow ?? AccountabiliBuddy.Core.Engine.StepAwayTimerPolicy.Default;
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
        _presenter = presenter;
        var exemptionDefaults = new ExemptionDefaultsService(documents);
        exemptionDefaults.LoadBlocking();
        presenter.PreTickedExemptionProfiles = () => exemptionDefaults.PreTicked;
        // ADR 59: an update closes the app itself, past the app's Exit event.
        var velopackUpdater = new VelopackUpdater { BeforeRestart = () => _quitGuard?.StandDown() };
        _updates = new UpdateService(
            velopackUpdater, _sessions, documents, presenter.ShowNotice, () => _mainWindow?.IsVisible == true);
        _updates.LoadBlocking();
        // How far escalation may go (the computer types' "notice and overlay only", ADR 22).
        var enforcementLimit = new EnforcementLimitService(documents);
        enforcementLimit.LoadBlocking();
        var enforcementEngine = new EnforcementEngine(new RuleEvaluator(new RuleMatcher())) { MaxLevel = enforcementLimit.MaxLevel };
        enforcementLimit.Changed += (_, level) => enforcementEngine.MaxLevel = level;
        // ADR 68: AccountabiliBuddy (guarding) or ProductaBuddy (companion only), one synced choice; the brand name follows it.
        var guidance = new GuidanceModeService(documents, _sessions!, () => _configuration!.Current.Locks.LooseningDelay, new DpapiDataProtector());
        guidance.LoadBlocking();
        _guidance = guidance;
        // ADR 68: "ProductaBuddy now" during a session waits what ending it early waits, in the background once confirmed (ADR 66).
        _armedEnds.Register(new CompanionArmedEndHandler(guidance));
        // ADR 69: some settings are kept per mode; their services read the active mode's copy.
        var modeSettings = new ModeSettings(documents, guidance);
        _modeSettings = modeSettings;
        // ADR 69, 77: the Tasks settings (actions per session, when I step away) are kept per mode, same defaults in both.
        _taskSettings?.UseModes(modeSettings);
        _runningWaits!.AddSource(() => guidance.State.Pending is GuidancePending pending
                && (pending.DuringSession || !_sessions!.State.IsActive || pending.ApplyAt > _sessions.Now)
            ? [new RunningWait("guidance", AppBrand.CompanionName, pending.ApplyAt, guidance.ChooseGuardingAsync)]
            : []);
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
            _privateAccess)
        {
            GuardedPlan = () => effectivePlan.Current,
            IsCompanion = () => guidance.IsCompanion,
        };

        var pendingChanges = new PendingChangesViewModel(_configuration, _sessions);
        var dayBarSettings = new DayBarSettingsService(documents);
        dayBarSettings.LoadBlocking();
        var overlaySettings = new OverlaySettingsService(documents) { Modes = modeSettings };
        overlaySettings.LoadBlocking();
        var sleepSettings = new SleepSettingsService(documents, time, _sessions!, () => _configuration!.Current.Locks.LooseningDelay);
        sleepSettings.LoadBlocking();
        // ADR 60: what happens to a session when the PC goes off during it.
        var pcOff = new PcOffSettingsService(documents, time, _sessions!, () => _configuration!.Current.Locks.LooseningDelay);
        pcOff.LoadBlocking();
        _pcOff = pcOff;
        _sessions!.OffSettings = () => pcOff.Current;
        _ = FollowPlanForSleepAsync(documents, sleepSettings);
        _bedtimeReminder = new BedtimeReminderService(sleepSettings, documents, time, () => _configuration!.Current.DayPlan);
        _bedtimeReminder.LoadBlocking();
        var omittedSessions = new OmittedSessionsService(documents);
        omittedSessions.LoadBlocking();
        var sessionGaps = new SessionGapsService(documents);
        sessionGaps.LoadBlocking();
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
        var soundSettings = new SessionSoundSettingsService(documents) { Modes = modeSettings };
        soundSettings.LoadBlocking();
        _soundPlayer = new Win32SoundPlayer();
        _sessionCues = new SessionCueService(_sessions, soundSettings, _soundPlayer);
        _taskbarGeometry = new AccountabiliBuddy.Platform.Windows.Taskbar.Win32TaskbarGeometry();
        _batteryReader = new AccountabiliBuddy.Platform.Windows.Power.WinRtBatteryReader();
        _ctrlAltWatcher = new AccountabiliBuddy.Platform.Windows.Hotkeys.Win32CtrlAltWatcher();
        _panelKeys = new AccountabiliBuddy.Platform.Windows.Hotkeys.Win32PanelKeys();
        _overlayStickyHotkey = new Win32GlobalHotkey();
        _overlayHideHotkey = new Win32GlobalHotkey();
        var batteryVisibility = new BatteryVisibilityService(documents);
        batteryVisibility.LoadBlocking();
        _sessionOverlay = new SessionOverlayService(
            _sessions, _configuration, overlaySettings, new Win32OverlayWindowControl(), _taskbarGeometry, _batteryReader,
            batteryVisibility, _ctrlAltWatcher, _panelKeys, _overlayStickyHotkey, _overlayHideHotkey, timelineDay, _sessionCues);
        _sessionOverlay.Actions.RunningWaitText = _runningWaits.Line;
        _dayBar = new DayBarViewModel(
            _configuration, _sessions, calendars, activityLog, dayBarSettings, sleepSettings, omittedSessions, _coldTurkey, deletedUsage, time);
        // ADR 35: this PC's alarm, when Settings → Sleep takes the wake time from it.
        _pcAlarm = new PcAlarmService(
            sleepSettings, _configuration, _sessionCues, new AccountabiliBuddy.Platform.Windows.Time.WindowsWakeTimer(), time, () => _sessionOverlay.Glow());
        _pcAlarm.Start();
        // Timers and alarms (V1, this PC only): a line each on the on-screen window; rung like a phase end (glow + chime).
        _timers = new AccountabiliBuddy.App.Services.Timers.TimersService(documents, _sessions);
        _timers.LoadBlocking();
        _sessionOverlay.SetTimers(_timers);
        _ = new AccountabiliBuddy.App.Services.Timers.TimerRinger(
            _timers,
            // Never silent: with "Focus ends" set to Off they ring with the bell.
            () => _sessionCues.Play(soundSettings.Current.FocusEndSound == SoundChoice.OffId ? SoundChoice.BellId : soundSettings.Current.FocusEndSound),
            () => _sessionOverlay.Glow());
        _dayBar.Gaps = sessionGaps;
        _noiseSettings = new NoiseSettingsService(documents);
        _noiseSettings.LoadBlocking();
        // One watcher for auto-play and the player's headphone lock; auto-play's service disposes it.
        var headphoneWatcher = new AccountabiliBuddy.Platform.Windows.Audio.WindowsHeadphoneWatcher();
        _noise = new NoisePlayerService(
            () => new AccountabiliBuddy.Platform.Windows.Audio.WindowsNoisePlayer(), _noiseSettings, () => new Win32GlobalHotkey(), headphoneWatcher);
        _noise.Start();
        _headphones = new HeadphoneAutoPlayService(documents, headphoneWatcher, _noise, time) { Modes = modeSettings };
        _headphones.PlanProvider = () => effectivePlan.Current;
        _headphones.LoadBlocking();
        var noisePlayer = new NoisePlayerViewModel(_noise);
        _sessionOverlay.SetNoisePlayer(noisePlayer);
        var tonightWake = new TonightWakeViewModel(sleepSettings, _configuration, time);
        tonightWake.AskToOverrideAlarm = (title, message, confirm) => presenter.ShowChoice(title, message, "Use my time", confirm, () => { });
        _sessionOverlay.SetTonightWake(tonightWake);
        _sessionOverlay.SetChecklist(new ChecklistViewModel(sessionTasks, ChecklistMode.Compact, time));
        _sessionOverlay.SetStepAway(_stepAway);
        // Plan first: a session's first 7 minutes hold unknowns (rules still enforce).
        // A fresh install's first session learns instead of asking (begun below once the setup assistant's state is known).
        var learnMode = new LearnModeService(documents);
        learnMode.LoadBlocking();
        _planFirst = new PlanFirstService(_sessions, sessionTasks, learnMode);
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
        // Planning time is up: the focus-end chime and the overlay's glow (the "Ready?" card follows).
        _planFirst.TimeUp += (_, _) => _sessionCues?.RaisePlanningUp();
        // Capture at the source: Ctrl+Alt+N parks an idea as a come-back (this computer, encrypted).
        _comebacks = new ComebackStore(documents, new DpapiDataProtector(), time);
        _comebacks.LoadBlocking();
        // A task can be parked as a come-back, and a come-back brought into the checklist.
        sessionTasks.Comebacks = _comebacks;
        // The Later list (ADR 50): "Move to Later" puts a task there; kept encrypted and shared like the next session's tasks.
        _later = new LaterStore(documents, new DpapiDataProtector(), time);
        _later.LoadBlocking();
        sessionTasks.Later = _later;
        // Recently deleted (ADR 50): what the user deletes stays 30 days on this computer only, sealed, so Undo and Restore can bring it back.
        var recentlyDeleted = new RecentlyDeletedStore(documents, new DpapiDataProtector(), time);
        recentlyDeleted.LoadBlocking();
        sessionTasks.Deleted = recentlyDeleted;
        // "Put in plan": a part's own start, from the day plan as it is now (else the time it was planned with).
        sessionTasks.PlannedStartOf = slot => PlannedPartStart(slot);
        // Notes kept in Focus mode (redesign phase 5): this computer only, sealed.
        _sessionNotes = new SessionNotesStore(documents, new DpapiDataProtector(), time);
        _sessionNotes.LoadBlocking();
        _capture = new CaptureService(_comebacks, new Win32GlobalHotkey(), monitorLocator);
        // ADR 27: work hours on this computer (off unless turned on in Settings).
        _workHours = new WorkHoursGuard(_sessions, _configuration, documents, windowsEnforcer, () => _activityWatcher.CaptureCurrent(), presenter.ShowNotice);
        _workHours.LoadBlocking();
        _armedEnds.Register(new StopForTodayArmedEndHandler(_workHours));
        // Hyperfocus guard: one quiet reminder after 90 minutes of unbroken focus.
        _hyperfocus = new HyperfocusGuard(_sessions, documents, (title, message) =>
            presenter.ShowReminder(title, message, new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2A9D8F"))))
        {
            Modes = modeSettings,
        };
        _hyperfocus.LoadBlocking();
        // Steer toward starting: Today with the task box ready when the user arrives in a work part, then the card.
        _startNudge = new StartNudgeService(_sessions, _configuration, new Win32UserIdleDetector(), documents,
            new StartNudgeCardLog(documents, new DpapiDataProtector()))
        {
            Modes = modeSettings,
        };
        _startNudge.LoadBlocking();
        // After a session the nudge only flashes the taskbar button (never takes focus).
        _windowAttention = new WindowsWindowAttention();
        // Flexible lunch (schema v8): Start lunch on any workday, on any device, for the lunch length; one chime when it's over.
        _lunch = new LunchService(_sessions, _configuration, documents,
            (title, message) => presenter.ShowReminder(title, message, new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#CA8A04"))),
            () => _sessionCues?.Play(soundSettings.Current.BreakEndSound),
            new Win32UserIdleDetector());
        _lunch.LoadBlocking();
        // A fixed lunch period starts by itself; a session that would run into it asks first (Keep, Adjust, Flexible this time).
        _lunchRunInto = new LunchRunIntoService(_sessions!, _lunch) { EndEarlyAsync = () => _endFlow!.TryEndSessionAsync(null) };
        _lunchRunInto.CardChanged += (_, _) => Dispatcher.UIThread.Post(() => ShowLunchRunIntoCard(monitorLocator));
        // Browsing at lunch isn't being back: lunch's step away ends with "I'm back" or the lunch ending, not input.
        _stepAway!.HoldsAway = () => _lunch.HoldsSessionAway;
        // Animations at the chosen speed, or none (Settings → On screen & sound).
        _motion = new Motion(documents);
        _motion.LoadBlocking();
        // The look (ADR 49): theme, light or dark (set times or sunset) and colour; the place for sunset is stored encrypted.
        var appTheme = new AppTheme(documents, _themes!, time, new DpapiDataProtector(), new WindowsLocationProvider()) { Modes = modeSettings };
        appTheme.LoadBlocking();
        // ADR 69: on a mode switch (or a compare-view change) every per-mode service reads the active mode's copy again.
        modeSettings.Reload += (_, _) =>
        {
            overlaySettings.ReloadForMode();
            soundSettings.ReloadForMode();
            _headphones.ReloadForMode();
            _hyperfocus.ReloadForMode();
            _startNudge.ReloadForMode();
            appTheme.ReloadForMode();
        };
        _appearance = appTheme;
        _motion.Apply();
        // ADR 28: while it guards, a day plan change that frees some of today's work waits like a looser change.
        _configuration.DayPlanHold = (before, after) => _workHours.DescribeCut(before, after) ?? _workHours.DescribeWeakening(before, after);
        _activityWatcher.ActivityChanged += (_, snapshot) => Dispatcher.UIThread.Post(() => _workHours.OnActivity(snapshot));
        // The gate: reaching for a dev tool once work starts, or after 3 idle minutes, asks what you were about to do (ADR 28).
        _workGate = new WorkGateService(_workHours, new Win32UserIdleDetector(), _comebacks, _sessions);
        // Lights out (design K, opt-in): lock or sleep at each work part's start and after 3 idle minutes.
        Func<bool> inCalendarEvent = () =>
        {
            DateTime now = time.GetLocalNow().DateTime;
            return calendars.EventsOn(DateOnly.FromDateTime(now)).Any(entry => !entry.Event.IsAllDay && entry.Event.LocalStart <= now && now < entry.Event.LocalEnd);
        };
        _lightsOut = new LightsOutService(
            documents, _workHours, _sessions, new Win32UserIdleDetector(), new AccountabiliBuddy.Platform.Windows.Activity.Win32ComputerLock(),
            inCalendarEvent: inCalendarEvent,
            isLocalOnly: () => _computerTypes?.Current == AccountabiliBuddy.App.Services.Setup.ComputerType.LocalOnly);
        _lightsOut.LoadBlocking();
        _workGate.Changed += (_, _) => ShowWorkGate(monitorLocator);
        // The start nudge's card: never in a calendar event or once today's goal is met.
        _startNudge.InCalendarEvent = inCalendarEvent;
        _startNudge.IsGoalMet = () => _deepWork?.Progress.Met == true;
        _startNudge.IsGateOpen = () => _workGate?.Current is not null;
        _startNudge.CardChanged += (_, _) => ShowStartNudgeCard(monitorLocator);
        _startNudge.AssistantCardChanged += (_, _) =>
        {
            ShowStartOfDayCard(monitorLocator);
            ShowLunchRunIntoCard(monitorLocator);
        };
        // ADR 31: Pre-Work parts ask whether today is a work day, then hold work tools and distractions back.
        _preWork = new PreWorkService(
            _sessions, _configuration, documents, new DpapiDataProtector(), new Win32UserIdleDetector(), windowsEnforcer,
            () => _activityWatcher.CaptureCurrent(),
            () => _configuration.Current.Profiles.FirstOrDefault(profile => profile.Id == _quickSession!.CountUpProfileId)
                ?? _configuration.Current.Profiles.FirstOrDefault(profile => profile.Name == _quickSession!.CountUpProfileName),
            () => _workHours.Settings.Apps.ToList(),
            presenter.ShowNotice);
        _preWork.LoadBlocking();
        _preWork.Changed += (_, _) => ShowPreWork(monitorLocator);
        _preWork.DayOffTaken += (_, _) => _ = _workHours.TakeDayOffAsync();
        // ADR 66: the day-off wait confirmed early; the planned lines travel sealed as the armed end's reason.
        _armedEnds.Register(new PreWorkDayOffArmedEndHandler(_preWork, _sessions, text => _comebacks!.AddAsync(text)));
        _preWork.DayOffArmed = () => _armedEnds.Find(ArmedEndKind.PreWorkDayOff) is not null;
        _preWork.ArmDayOffHandler = (askedAt, plans) =>
            _armedEnds.Arm(ArmedEndKind.PreWorkDayOff, askedAt, PreWorkService.DayOffWait, plans) is not null;
        // Nothing steers on a day off or during lunch.
        _startNudge!.IsDayOff = () => _preWork.IsDayOff || _lunch!.IsOnLunch;
        _startNudge.IsWorkOver = () => _shutdown?.IsWorkOver ?? false;
        _activityWatcher.ActivityChanged += (_, snapshot) => Dispatcher.UIThread.Post(() => _preWork.OnActivity(snapshot));
        // The review card after each session: what it used with no rule, as rules.
        _sessionReview = new SessionReviewService(activityLog, _configuration, documents, _sessions, () => exemptionDefaults.PreTicked)
        {
            NotesBetween = _sessionNotes.Between,
            OverLimitCount = sessionTasks.OverLimitCount,
            PhoneCounting = () => _phonePickups?.IsCounting == true,
        };
        _sessionReview.LoadBlocking();
        // More than 3 tasks when a real session starts: pick 3, the rest are parked (no way to skip).
        sessionTasks.PickRequired += (_, _) => Dispatcher.UIThread.Post(() => ShowPickTasks(sessionTasks, monitorLocator));
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
        // Beeminder (ADR 43): read only, through CalendarNetwork, only once the user pastes a token.
        _beeminder = new AccountabiliBuddy.App.Services.Beeminder.BeeminderService(_calendarNetwork, documents, dataProtector, time);
        _beeminder.LoadBlocking();
        // A phone starting, ending early or editing the plan (ADR 19, 42), through the sync files.
        var planEditTime = new DayPlanEditTimeService(_configuration, documents, () => _sessions.Now);
        planEditTime.LoadBlocking();
        var remoteControl = new AccountabiliBuddy.App.Services.Remote.RemoteSessionControl(_sessions, _configuration, _quickSession);
        var remotePlanEdits = new AccountabiliBuddy.App.Services.Remote.RemotePlanEdits(_sessions, _configuration, planEditTime);
        // Sharing progress between the user's own devices (ADR 22): every method is off until turned on.
        var syncSettings = new AccountabiliBuddy.App.Services.Sync.SyncSettingsService(documents, dataProtector);
        syncSettings.LoadBlocking();
        var receivedDevices = new AccountabiliBuddy.App.Services.Sync.ReceivedDevicesService(documents, time);
        receivedDevices.LoadBlocking();
        // The daily steps a paired phone read from Health Connect, from its sync file: this PC never reads Health Connect.
        var dailySteps = new AccountabiliBuddy.App.Services.DailyStepsService(documents, time);
        dailySteps.LoadBlocking();
        // A paired phone's next alarm (ADR 23): the newest phone file's, while it's still ahead.
        void FollowPhoneAlarm()
        {
            dailySteps.Follow(receivedDevices.Active);
            sleepSettings.SetPhoneAlarm(AccountabiliBuddy.App.Services.Sync.PhoneAlarm.Of(receivedDevices.Active, time));
            // ADR 34: the phone's smart bedtime, from the same files.
            sleepSettings.SetPhoneBedtime(AccountabiliBuddy.App.Services.Sync.PhoneBedtime.Of(receivedDevices.Active));
            // ADR 40: the phone's estimated wake per kind of morning, for mornings with no alarm known.
            sleepSettings.SetPhoneWake(AccountabiliBuddy.App.Services.Sync.PhoneWake.Of(receivedDevices.Active));
        }

        receivedDevices.Changed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(FollowPhoneAlarm);
        FollowPhoneAlarm();
        var syncIdentity = new AccountabiliBuddy.App.Services.Sync.SyncIdentityService(documents, dataProtector, receivedDevices);
        // The phone's pickups during this PC's sessions (ADR 26), from its sync file.
        _phonePickups = new AccountabiliBuddy.App.Services.Sync.PhonePickups(receivedDevices, syncIdentity, time);
        syncIdentity.LoadBlocking();
        _bedtimeReminder!.DeviceId = () => syncIdentity.DeviceId == Guid.Empty ? null : syncIdentity.DeviceId;
        var goal = new GoalService(documents);
        goal.LoadBlocking();
        var syncEditTimes = new AccountabiliBuddy.App.Services.Sync.SyncEditTimesService(_configuration, sleepSettings, dayBarSettings, goal, documents, time);
        syncEditTimes.LoadBlocking();
        var syncCoordinator = _syncCoordinator = new AccountabiliBuddy.App.Services.Sync.SyncCoordinator(
            _sessions, _configuration, activityLog, omittedSessions, deletedUsage, privacy, sleepSettings, dayBarSettings, goal, documents, syncSettings,
            syncIdentity, receivedDevices, syncEditTimes);
        syncCoordinator.Gaps = sessionGaps;
        // Guided sessions (ADR 24): the checklist goes to other devices in this PC's sync file, each task
        // with its category's name and colour.
        syncCoordinator.TasksOf = start => sessionTasks.ForSession(start) is { HasTasks: true } checklist ? sessionTasks.LabelledForSync(checklist) : null;
        sessionTasks.Changed += (_, _) =>
        {
            syncCoordinator.NotifyHistoryChanged();
        };
        // Come-backs (ADR 30): the open ones go to other devices in this PC's sync file.
        syncCoordinator.Comebacks = () => _comebacks!.Open.Select(comeback => new AccountabiliBuddy.Core.Sync.SyncComeback(comeback.Id, comeback.Text, comeback.CreatedAt)).ToList();
        // Shared lists (ADR 48): the next session's tasks and the come-backs go in this PC's sync file with their tombstones, and
        // another device's lists merge in item by item (the stores' own change events write the file again when they changed).
        // ADR 71: per PC, on by default; off, this PC's lists stay here and the others' don't come in.
        syncCoordinator.ShareTaskLists = () => syncSettings.Current.ShareTaskLists;
        syncCoordinator.NextTasks = sessionTasks.NextTasksForSync;
        syncCoordinator.Parked = _comebacks.ForSync;
        syncCoordinator.MergeNextTasks = sessionTasks.MergeNextTasksAsync;
        syncCoordinator.MergeParked = _comebacks.MergeAsync;
        // ADR 50: the Later list goes the same way.
        syncCoordinator.Later = _later.ForSync;
        syncCoordinator.MergeLater = _later.MergeAsync;
        // A fixed lunch doesn't start by itself while another PC runs a session (its file says so).
        _lunch!.OtherPcSessionRunning = () => AccountabiliBuddy.App.Services.Sync.OtherPcSessions.IsRunning(receivedDevices.Active, time.GetUtcNow());
        // Beeminder (ADR 43): only the derived summary goes in this PC's sync file; removing the token removes it.
        syncCoordinator.Beeminder = _beeminder.SyncSummary;
        IReadOnlyList<AccountabiliBuddy.Core.Sync.SyncBeeminderGoal>? lastBeeminder = _beeminder.SyncSummary();
        _beeminder.Changed += (_, _) =>
        {
            // Only a changed summary rewrites the file, not every 30-minute read that found the same goals.
            IReadOnlyList<AccountabiliBuddy.Core.Sync.SyncBeeminderGoal>? summary = _beeminder.SyncSummary();
            if ((summary ?? []).SequenceEqual(lastBeeminder ?? []) && (summary is null) == (lastBeeminder is null))
            {
                return;
            }

            lastBeeminder = summary;
            syncCoordinator.NotifyHistoryChanged();
        };
        // A manual Refresh/Test seals the file again so the phone gets the new goals at its next sync.
        _beeminder.ReadOnRequest += (_, _) => syncCoordinator.NotifyHistoryChanged();
        // Lunch is the user's, not this PC's: started here or on another device, every device takes the newest.
        syncCoordinator.Lunch = () => _lunch!.Taken;
        // What the user did on the phone for this PC (ADR 26): applied once each, acknowledged in this PC's file.
        var remoteNoise = new AccountabiliBuddy.App.Services.Remote.RemoteNoiseControl(_noise!, time);
        var syncActions = new AccountabiliBuddy.App.Services.Sync.SyncActionsService(
            documents, receivedDevices, syncIdentity, sessionTasks, _comebacks!, _stepAway!, _sessions, remoteControl, remotePlanEdits, remoteNoise, time);
        syncActions.LoadBlocking();
        // ADR 36, 42: the noise player in this PC's file for the phone and watch; a change goes out with the next write, and
        // playing or a phone's command speeds the Drive poll up.
        syncCoordinator.Noise = remoteNoise.StateAsync;
        remoteNoise.StateChanged += (_, _) =>
        {
            syncCoordinator.NotifyHistoryChanged();
            _syncDrive?.UpdatePollInterval();
            _syncRelay?.UpdatePollInterval();
        };
        syncActions.Changed += (_, _) =>
        {
            _syncDrive?.UpdatePollInterval();
            _syncRelay?.UpdatePollInterval();
        };
        syncCoordinator.Acks = () => syncActions.Acks;
        // ADR 42: the answers, the end-early offer and the running session's task and profiles, for the phone.
        syncCoordinator.ActionResults = () => syncActions.Results;
        syncCoordinator.EndEarlyOffer = () => syncActions.EndEarlyOffer;
        syncCoordinator.CurrentTask = () => sessionTasks.IsLive ? sessionTasks.Current.CurrentId : null;
        syncCoordinator.CountUpProfileId = () => _quickSession.CountUpProfileId;
        _quickSession.CountUpProfileChanged += (_, _) => syncCoordinator.NotifyHistoryChanged();
        // Pickups across PCs (design K): back at this PC during another PC's session, or straight from it during work hours.
        var pcPickups = new AccountabiliBuddy.App.Services.Sync.PcPickupsService(
            documents, _sessions, new Win32UserIdleDetector(), receivedDevices, syncIdentity,
            isGuarded: () => _workHours?.IsGuarding == true, notify: presenter.ShowNotice);
        pcPickups.LoadBlocking();
        syncCoordinator.PcPickups = pcPickups;
        pcPickups.Changed += (_, _) => syncCoordinator.NotifyHistoryChanged();
        // Phone placement: nags when the phone is out or in a pocket during a real session.
        _phonePlacement = new AccountabiliBuddy.App.Services.Sync.PhonePlacementService(
            _sessions, receivedDevices, () => sessionTasks.IsLive ? sessionTasks.Current.SessionStart : null,
            notify: presenter.ShowNotice,
            remind: (title, message) => presenter.ShowReminder(title, message, new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#CA8A04"))));
        // Calendar events in this PC's file too (ADR 26), so the phone's Calendar row stays current on Drive alone.
        syncCoordinator.Events = () => System.Text.Json.JsonSerializer.SerializeToElement(
            AccountabiliBuddy.App.Services.Sync.DeviceCalendarEvents.Build(
                calendars, DateOnly.FromDateTime(time.GetLocalNow().DateTime), _configuration!.Current.DayPlan, calendarOverlay));
        calendars.Changed += (_, _) => syncCoordinator.NotifyHistoryChanged();
        // A classification or rule that changes what the file says goes out soon (not for first-known stamps); posted because guards can raise it from other threads.
        calendarOverlay.Changed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(syncCoordinator.NotifyHistoryChanged);
        syncActions.Changed += (_, _) => syncCoordinator.NotifyHistoryChanged();
        receivedDevices.Changed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = syncActions.ApplyAsync());
        // A lunch change (started, +5/−5, ended here, or taken from another device's file) also writes the sync folder soon, whatever the
        // interval. Only when the lunch itself changed, and a received lunch that is already ours changes nothing, so two devices can't ping-pong.
        AccountabiliBuddy.Core.Sync.SyncLunch? lastLunch = _lunch!.Taken;
        _lunch.Changed += (_, _) =>
        {
            syncCoordinator.NotifyHistoryChanged();
            AccountabiliBuddy.Core.Sync.SyncLunch? taken = _lunch.Taken;
            if (taken != lastLunch)
            {
                lastLunch = taken;
                _syncFolder?.WriteSoon();
            }
        };
        receivedDevices.Changed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            bool taken = false;
            foreach (AccountabiliBuddy.Core.Sync.SyncLunch lunch in receivedDevices.Active.Select(device => device.File.Lunch).OfType<AccountabiliBuddy.Core.Sync.SyncLunch>())
            {
                taken |= _lunch.Receive(lunch);
            }

            // The day bar redrew when the file arrived, before the lunch was taken: once more now, not at its next 30-second redraw.
            if (taken)
            {
                _dayBar?.Redraw();
            }
        });
        _dayBar.Lunch = () => _lunch.Taken;
        // ADR 38: bedtime reminders dismissed or snoozed here go in this PC's file; the others' stop them ringing here.
        var reminderActions = new List<AccountabiliBuddy.Core.Sync.SyncReminderAction>();
        var appliedReminderActions = new HashSet<string>();
        syncCoordinator.ReminderActions = () => reminderActions.Where(a => a.At > time.GetUtcNow().AddDays(-1)).ToList();
        _bedtimeReminder!.ActedHere += (_, acted) =>
        {
            var entry = new AccountabiliBuddy.Core.Sync.SyncReminderAction(acted.Night, acted.Number, acted.Action, time.GetUtcNow(), acted.Until);
            appliedReminderActions.Add($"{entry.Night}/{entry.Number}/{entry.Action}/{entry.At.ToUnixTimeMilliseconds()}");
            reminderActions.Add(entry);
            syncCoordinator.NotifyHistoryChanged();
        };
        receivedDevices.Changed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            foreach (var entry in receivedDevices.Active.SelectMany(device => device.File.ReminderActions ?? Array.Empty<AccountabiliBuddy.Core.Sync.SyncReminderAction>()))
            {
                string key = $"{entry.Night}/{entry.Number}/{entry.Action}/{entry.At.ToUnixTimeMilliseconds()}";
                if (entry.At < time.GetUtcNow().AddHours(-1) || entry.Night != _bedtimeReminder.Night || !appliedReminderActions.Add(key))
                {
                    continue;
                }

                if (entry.Action == "snooze") _bedtimeReminder.SnoozeIt(entry.Number, here: false, entry.Until);
                else _bedtimeReminder.Dismiss(entry.Number, here: false);
            }
        });
        timelineDay.Lunch = () => _lunch.Taken;
        _comebacks!.Changed += (_, _) =>
        {
            syncCoordinator.NotifyHistoryChanged();
        };
        syncCoordinator.LoadBlocking();
        // Sync troubleshooting (SYNC1b, ADR 86): what arrived by which method and what each part came to, this PC's own puts, the files that aren't
        // taken in, and the rolling sync log (counts and times only). All device-local ("sync." documents, sync.log), in no backup.
        var syncReceiptsStore = new AccountabiliBuddy.App.Services.Sync.SyncReceiptsStore(documents);
        syncReceiptsStore.LoadBlocking();
        var syncSendLog = new AccountabiliBuddy.App.Services.Sync.SyncSendLog(documents);
        syncSendLog.LoadBlocking();
        syncCoordinator.Receipts = syncReceiptsStore;
        syncCoordinator.SendLog = syncSendLog;
        syncCoordinator.Unreadable = new AccountabiliBuddy.App.Services.Sync.SyncUnreadableFiles(time);
        syncCoordinator.SyncLogger = new AccountabiliBuddy.App.Services.Sync.SyncLog(Path.Combine(dataDirectory, "sync.log"), time);
        // The running log (ADR 22): the other devices' sessions on both day bars (History gets them below).
        _dayBar.UseReceived(receivedDevices);
        _dayBar.UseTasks(sessionTasks);
        timelineDay.UseTasks(sessionTasks);
        timelineDay.UseReceived(receivedDevices);
        _syncFolder = new AccountabiliBuddy.App.Services.Sync.SyncFolderService(syncCoordinator, syncSettings, syncIdentity, receivedDevices, _sessions);
        // Google Drive (ADR 26): signed in with the user's own Google client, drive.file only, through CalendarNetwork.
        GoogleEndpoints googleEndpoints = GoogleEndpointsFromEnvironment();
        var driveAccount = new AccountabiliBuddy.App.Services.Sync.DriveAccountService(
            new GoogleCalendarClient(_calendarNetwork, googleEndpoints, time), calendars, documents, dataProtector, time);
        driveAccount.LoadBlocking();
        var driveFiles = new AccountabiliBuddy.App.Services.Sync.GoogleDriveFiles(_calendarNetwork, googleEndpoints, driveAccount.GetAccessTokenAsync);
        _syncDrive = new AccountabiliBuddy.App.Services.Sync.SyncDriveService(
            syncCoordinator, syncSettings, syncIdentity, receivedDevices, _sessions, driveFiles)
        {
            IsSignedIn = () => driveAccount.IsSignedIn,
            NoiseBusy = () => remoteNoise.WantsFastPoll,
            // Every 10 seconds during work parts too, for pickups across PCs (design K).
            InWorkHours = () =>
            {
                DateTime now = time.GetLocalNow().DateTime;
                return DayWorkWindows.WorkWindows(
                        effectivePlan.Current, new LocalWindow(now.Date.AddDays(-1), now.Date.AddDays(2)), now, _workHours?.FocusedOn, _workHours?.NightFallback,
                        _workEnded?.Windows)
                    .Any(focus => focus.Window.Start <= now && now < focus.Window.End);
            },
        };
        driveAccount.Changed += (_, _) => _syncDrive.Restart();
        // The own relay (ADR 46): sealed files over the user's HTTPS relay, pushed in about a second, through CalendarNetwork.
        _syncRelay = new AccountabiliBuddy.App.Services.Sync.SyncRelayService(
            syncCoordinator, syncSettings, syncIdentity, receivedDevices, _sessions, _calendarNetwork)
        {
            NoiseBusy = _syncDrive.NoiseBusy,
            InWorkHours = _syncDrive.InWorkHours,
        };
        // Removing devices from the group and learning of a removal (ADR 54): the rekey file travels by every method in use.
        _syncRemoval = new AccountabiliBuddy.App.Services.Sync.SyncRemovalService(documents, syncIdentity, receivedDevices, _sessions)
        {
            Publishers = [_syncFolder, _syncDrive, _syncRelay],
        };
        _syncRemoval.LoadBlocking();
        syncCoordinator.Removal = _syncRemoval;
        // Backups of a removed device and restoring them (ADR 55); History, Stats and the day bar label removed devices.
        var deviceBackups = new AccountabiliBuddy.App.Services.Sync.DeviceBackupService(driveFiles, syncIdentity, syncSettings, () => driveAccount.IsSignedIn);
        // The group's past keys (ADR 56): kept on every key switch, shared in the sealed sync files, used only to open backups.
        var pastKeys = new AccountabiliBuddy.App.Services.Sync.PastKeysService(documents, dataProtector, time);
        pastKeys.LoadBlocking();
        syncIdentity.KeySwitched += (_, switched) => pastKeys.NoteSwitched(switched.OldKey);
        syncCoordinator.PastKeys = pastKeys;
        var backupRestore = new AccountabiliBuddy.App.Services.Sync.BackupRestoreService(deviceBackups, syncIdentity, receivedDevices, _syncRemoval, pastKeys);
        receivedDevices.IsRemoved = _syncRemoval.IsRemoved;
        // The first FollowPhoneAlarm ran before IsRemoved was set, so a removed phone's alarm, bedtime, wake and steps showed after a restart.
        FollowPhoneAlarm();
        // Start over / Start from scratch (ADR 54, 10): the next start clears the data (see StartOverService).
        var startOver = new AccountabiliBuddy.App.Services.Setup.StartOverService(database, _sessions, dataDirectory);
        Action openStartOver = () => ShowOwnedDialog(new StartOverDialog { DataContext = new StartOverViewModel(startOver, _sessions, TryRestartApp) });
        _unlinkedViewModel = new UnlinkedViewModel(_syncRemoval, _sessions, time, openStartOver);
        _unlinkedViewModel.Resolved += (_, _) => _unlinkedShownFor = null;
        _syncRemoval.Changed += (_, _) => Dispatcher.UIThread.Post(ShowUnlinkedIfNew);
        // A removed phone stops driving bedtime, wake and the alarm (ReceivedDevicesService.Active).
        _syncRemoval.Changed += (_, _) => Dispatcher.UIThread.Post(FollowPhoneAlarm);
        // Task categories from every device: this PC lists its own in its sync file, and the "Add a task" pickers group the other
        // devices' by name (a removed device's are gone: Active). The pickers look again when a device's list or the group changes.
        syncCoordinator.Categories = () => sessionTasks.Categories.Select(category => new AccountabiliBuddy.Core.Sync.SyncCategory(category.Name, category.Color, category.ColorEditedAt)).ToList();
        sessionTasks.OtherDeviceCategories = () => receivedDevices.Active
            .Where(device => syncSettings.Current.ShareTaskLists && device.File.Categories is { Count: > 0 })
            .OrderBy(device => device.File.Device.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(device => new DeviceCategories(device.File.Device.DeviceId, device.File.Device.Name, device.File.Categories!))
            .ToList();
        receivedDevices.Changed += (_, _) => Dispatcher.UIThread.Post(sessionTasks.NotifyOtherCategoriesChanged);
        _syncRemoval.Changed += (_, _) => Dispatcher.UIThread.Post(sessionTasks.NotifyOtherCategoriesChanged);
        Dispatcher.UIThread.Post(sessionTasks.NotifyOtherCategoriesChanged);

        // Run a relay on this PC (ADR 46): the relay program ships next to the app, lives in the app's data folder and ends with the app.
        _relayHost = new AccountabiliBuddy.App.Services.Sync.RelayHostService(
            syncSettings, new WindowsRelayProcess(), new WindowsRelayFirewall(),
            new AccountabiliBuddy.App.Services.Sync.RelayHostFiles(Path.Combine(dataDirectory, "relay")), AppPaths.RelayPath);

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
            IsCompanion = guidance.IsCompanion,
        };
        modeSettings.Reload += (_, _) => setupViewModel.IsCompanion = guidance.IsCompanion;
        _setupAssistant = new SetupAssistantService(documents, time);
        _setupAssistant.LoadBlocking();
        _ = learnMode.BeginIfFirstRunAsync(_setupAssistant.HasBeenShown);
        // ADR 29: the daily goal goes to other devices with today's focus.
        syncCoordinator.GoalMinutes = () => goal.Current.DailyMinutes;
        var blockerLocator = new WindowsBlockerSourceLocator();
        ConfigurationService configuration = _configuration;
        SessionController sessions = _sessions;
        var backups = new BackupService(_configuration, documents, activityLog, new DpapiDataProtector(), time, dataDirectory)
        {
            // ADR 71: synced settings and task lists back up and restore through their own services.
            Synced = AccountabiliBuddy.App.Services.Backup.BackupAccess.Synced(sleepSettings, dayBarSettings, goal, guidance, minutes => SetDailyGoalAsync(goal, minutes)),
            Tasks = AccountabiliBuddy.App.Services.Backup.BackupAccess.Tasks(sessionTasks, _comebacks!, _later!, time, taskStore, taskAccount),
            ShareTaskLists = () => syncSettings.Current.ShareTaskLists,
        };
        // ADR 71: auto-backup of the synced settings, this PC's settings and (unshared) task lists, local and sealed on Drive.
        _autoBackup = new AccountabiliBuddy.App.Services.Backup.AutoBackupService(
            backups, documents, driveFiles, syncIdentity, syncSettings, () => driveAccount.IsSignedIn, time);
        _autoBackup.LoadBlocking();
#if DEBUG
        // ADR 71: every stored document must belong to a backup list or the state list; name any that don't.
        _ = Task.Run(async () =>
        {
            IReadOnlyList<string> unclassified = await backups.UnclassifiedKeysAsync();
            if (unclassified.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine("Backup registry: unclassified documents: " + string.Join(", ", unclassified));
            }
        });
#endif
        // The computer types (ADR 22): the assistant applies one; Settings shows it and can apply one again.
        var computerTypes = new AccountabiliBuddy.App.Services.Setup.ComputerTypeService(documents, privacy, enforcementLimit, syncSettings, _updates);
        _computerTypes = computerTypes;
        computerTypes.LoadBlocking();
        _createAssistant = () => new SetupAssistantViewModel(
            blockerLocator, configuration, sessions, activityLog, privacy, time, setupViewModel,
            new BackupViewModel(backups, sessions, RestartApp, showExport: false))
        {
            Types = computerTypes,
            Identity = syncIdentity,
            Restore = backupRestore,
            ReadOtherDevicesNow = async () =>
            {
                await _syncFolder!.ReadNowAsync();
                await _syncDrive!.ReadNowAsync();
                await _syncRelay!.ReadNowAsync();
            },
            HasSyncMethod = () => syncSettings.Current is { } sync
                && ((sync.FolderOn && !string.IsNullOrWhiteSpace(sync.FolderPath)) || sync.DriveOn || sync.RelayOn),
            Goal = goal,
            SetDailyGoal = minutes => SetDailyGoalAsync(goal, minutes),
            StartNudge = _startNudge,
            Appearance = new AppearanceViewModel(appTheme),
            WorkHours = _workHours,
            Keys = _coldTurkeyKeys,
            QuitGuard = _quitGuardViewModel,
            Guidance = _guidanceViewModel,
            ColdTurkey = _coldTurkey,
            OtherBlockers = new WindowsOtherBlockerDetector(),
            OpenDpAssistant = () => _showDpAssistant?.Invoke(),
            OpenCalendarRules = () => _showCalendarRules?.Invoke(),
            // ADR 76, 78 (Phase 8): the Tasks redesign's pages, only with the task list in use. A new chooser per assistant (its picked file).
            TaskViewChooser = _taskAccount is not null && taskStore.IsActive
                ? new TaskViewChooserViewModel(_taskAccount, _taskViews, backups.Read, time)
                : null,
            TaskRingDevices = taskStore.IsActive ? _taskReminderAlerts : null,
            TaskExport = taskStore.IsActive && _taskCalendarExport is not null && _taskReminderSettings is not null
                ? new TaskExportSetupViewModel(_taskCalendarExport, _taskReminderSettings) { OpenBrowser = OpenInBrowser }
                : null,
            SkipFeature = id => _setupFeatures?.SkipAsync(id) ?? Task.CompletedTask,
            Goals = _goals is { Store.IsReady: true } ? _goals : null,
            TodayLayout = _todayLayout,
            StepsMoved = _stepsMovedCard,
        };
        setupViewModel.OpenAssistant = () => ShowSetupAssistant(quick: false);

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

        // The Devices tab (ADR 22): the group and the sharing methods.
        // The sync light's setting and which batteries show where (both this PC only, documents "syncLight" and "batteryVisibility").
        var syncLightSetting = new SyncLightSettingService(documents);
        syncLightSetting.LoadBlocking();
        // Sync receipts (developer switch, off by default): a notice when another device's file shows it has this PC's newer file.
        var syncReceipts = new AccountabiliBuddy.App.Services.Sync.SyncReceiptsService(receivedDevices, syncIdentity, documents, time, presenter.ShowNotice);
        syncReceipts.LoadBlocking();
        receivedDevices.Changed += (_, _) => Dispatcher.UIThread.Post(() => syncReceipts.Check());
        var devices = new DevicesViewModel(syncIdentity, syncSettings, receivedDevices, syncCoordinator, _sessions, time)
        {
            SyncLightSetting = syncLightSetting,
            Batteries = batteryVisibility,
            Removal = _syncRemoval,
            PastKeys = pastKeys,
            UnlinkedPanel = _unlinkedViewModel,
        };
        // Remove this device… (ADR 54, 55): the dialog works through the removal service and the backup service.
        devices.OpenRemoveDialog = deviceId => ShowOwnedDialog(new RemoveDeviceDialog
        {
            DataContext = new RemoveDeviceViewModel(_syncRemoval!, receivedDevices, deviceBackups, time, deviceId),
        });
        var sessionViewModel = new SessionViewModel(
            _sessions, _configuration, _endFlow, VisibleMainWindow, new UnlockViewModel(_sessions, _configuration, _unlocks), _quickSession)
        {
            TimerToggle = new TimerToggleViewModel(_sessionOverlay),
            OverlayStatus = new OverlayStatusViewModel(overlaySettings, _sessionOverlay),
            RunningWaits = new RunningWaitsViewModel(_runningWaits!, _sessions),
            Day = timelineDay,
            ColdTurkey = new ColdTurkeyStatusViewModel(_coldTurkey, _sessions),
        };
        // Focus mode: the Today page while a session runs (the main window's day bar is the one it reads).
        sessionViewModel.Comebacks = new ComebacksViewModel(_comebacks!, sessionTasks);
        sessionViewModel.Timers = new TimersViewModel(_timers!, _sessions);
        sessionViewModel.Lunch = new LunchCardViewModel(_lunch!, _sessions)
        {
            // The lunch card's "On all your devices" line: the other PCs' and phones' latest files (a watch writes none; removed devices are not in Active).
            Peers = () => receivedDevices.Active
                .Where(device => device.File.Device.Kind != AccountabiliBuddy.Core.Sync.SyncDeviceCard.KindWatch)
                .Select(device => new AccountabiliBuddy.Core.Sync.LunchPeer(device.File.Device.Name, device.File.Device.WrittenAt, device.File.Lunch))
                .ToList(),
        };
        receivedDevices.Changed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(sessionViewModel.Lunch.Refresh);
        sessionViewModel.DayOff = new DayOffCardViewModel(_preWork!);
        var beeminderViewModel = new BeeminderViewModel(_beeminder!, time);
        sessionViewModel.Beeminder = beeminderViewModel;
        // Deep work against the daily goal (redesign phase 4): Today's card, the on-screen details, "Goal met" once a day.
        _deepWork = new DeepWorkService(
            _sessions,
            goal,
            async () =>
            {
                DateTimeOffset now = time.GetUtcNow();
                return sessionGaps.Apply(SessionHistoryBuilder.Build(await activityLog.GetSessionEventsAsync(now.AddDays(-2), now.AddDays(1))));
            },
            () => AccountabiliBuddy.App.Services.Sync.ReceivedSessions.Closed(receivedDevices.Devices),
            omittedSessions.IsOmitted);
        // Read again once the log has the session's start or end (not on the state change, which comes first).
        _recorder!.LogChanged += (_, _) => Dispatcher.UIThread.Post(() => _deepWork?.NotifyLogChanged());
        receivedDevices.Changed += (_, _) => Dispatcher.UIThread.Post(() => _deepWork?.NotifyLogChanged());
        goal.Changed += (_, _) => syncCoordinator.NotifyHistoryChanged();
        _deepWork.GoalReached += (_, progress) =>
            presenter.ShowReminder(progress.Text, "Today's deep-work goal is reached. Anything more is a bonus.",
                new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2A9D8F")));
        // A Budget day's pace (ADR 47): the budget left and the day left for it, read from the day plan, the counted focus and the clock.
        Func<BudgetPace?> budgetPace = () =>
        {
            DateTime now = time.GetLocalNow().DateTime;
            DateOnly today = DateOnly.FromDateTime(now);
            return DayWorkWindows.Pace(effectivePlan.Current, today, _deepWork?.FocusedOn(today) ?? TimeSpan.Zero, now, _workHours?.NightFallback, _workEnded?.Windows);
        };
        Func<string> goalOverlayText = () => budgetPace() is BudgetPace pace
            ? $"Goal · {_deepWork.Progress.Text} · {BudgetPaceText.Describe(pace)}"
            : $"Goal · {_deepWork.Progress.Text}";
        _deepWork.Changed += (_, _) => _sessionOverlay?.SetGoalText(goalOverlayText());
        Func<bool> goalLowerWaits = () => _workHours?.HasWorkLeftToday == true;
        syncCoordinator.GoalLowerWaits = goalLowerWaits;
        // The start-of-day assistant (first arrival of a workday): today's hours, first tasks, Start. Lowering the goal waits like on the goal card.
        var startOfDay = new StartOfDayAssistant(documents, goal, _configuration, sessionTasks, _quickSession!, _sessions)
        {
            GoalLowerWaits = goalLowerWaits,
            RefreshProgress = () => _deepWork?.CheckNow(),
        };
        startOfDay.LoadBlocking();
        _startNudge!.Assistant = startOfDay;
        var dpAssistant = new DpAssistantService(documents);
        dpAssistant.LoadBlocking();
        sessionViewModel.Goal = new GoalCardViewModel(_deepWork, goal)
        {
            LowerWaits = goalLowerWaits,
            PhoneLine = _phonePickups!.SummaryText,
            PaceNow = budgetPace,
        };
        sessionViewModel.Goal.RefreshPhone();
        sessionViewModel.Goal.RefreshPace();
        // The pace moves with the clock and the plan: the overlay's goal line follows the card's refreshes.
        sessionViewModel.Goal.PaceRefreshed += (_, _) => _sessionOverlay?.SetGoalText(goalOverlayText());
        _configuration.Changed += (_, _) => sessionViewModel.Goal?.RefreshPace();
        sessionViewModel.Week = new WeekCardViewModel(_configuration, _sessions, () => DateOnly.FromDateTime(time.GetLocalNow().DateTime));
        _goalCard = sessionViewModel.Goal;
        _weekCard = sessionViewModel.Week;
        // A phone's file arrived: its pickups show on Today, the review card and the Focus row.
        receivedDevices.Changed += (_, _) =>
        {
            sessionViewModel.Goal?.RefreshPhone();
            if (sessionViewModel.Review is { SessionStart: DateTimeOffset reviewed } card)
            {
                card.PhoneText = _phonePickups.Describe(reviewed) ?? string.Empty;
            }
        };
        _dayBar.PhoneFreeSessions = _phonePickups.PhoneFreeSessions;
        timelineDay.PhoneFreeSessions = _phonePickups.PhoneFreeSessions;
        // The shutdown routine (redesign phase 5): one card on Today once the day's work is over.
        // A card left open closes by itself; its typed tomorrow's first task waits under the Tasks page's "Auto-saved" (this computer, encrypted).
        var autoSaved = new AutoSavedTaskStore(documents, new DpapiDataProtector(), time);
        autoSaved.LoadBlocking();
        sessionTasks.AutoSaved = autoSaved;
        // ADR 72: every task list syncs. The history of past sessions, the shared record of what was done with their open tasks,
        // Auto-saved, and local items handed to the one device that keeps them; other PCs' history kept sealed here.
        Func<Guid?> selfId = () => syncIdentity.DeviceId == Guid.Empty ? null : syncIdentity.DeviceId;
        sessionTasks.SelfId = selfId;
        _later!.SelfId = selfId;
        _comebacks!.SelfId = selfId;
        autoSaved.SelfId = selfId;
        var pastSettled = new PastSettledStore(documents, time);
        pastSettled.LoadBlocking();
        sessionTasks.PastSettled = pastSettled;
        _ = pastSettled.ImportMovedAsync(sessionTasks.LegacyMovedEntries());
        var receivedHistory = new ReceivedTaskHistoryStore(documents, new DpapiDataProtector(), time);
        receivedHistory.LoadBlocking();
        var handovers = new HandoverOutbox(documents, new DpapiDataProtector(), time);
        handovers.LoadBlocking();
        var keptPast = new KeptPastStore(documents, new DpapiDataProtector(), time);
        keptPast.LoadBlocking();
        _taskListSync = new AccountabiliBuddy.App.Services.Sync.TaskListSync(
            sessionTasks, pastSettled, autoSaved, receivedHistory, handovers, keptPast, _later, _comebacks, selfId, () => syncIdentity.DeviceName, time);
        _receivedHistory = receivedHistory;
        _keptPast = keptPast;
        _pastSettled = pastSettled;
        sessionTasks.Kept = keptPast;
        sessionTasks.ForeignTaskOf = id => keptPast.Find(id)?.Task ?? receivedHistory.Find(id)?.Task;
        sessionTasks.OtherPastUnfinished = () =>
        {
            DateTimeOffset since = time.GetUtcNow() - TimeSpan.FromDays(14);
            return receivedHistory.Unfinished(since, pastSettled.Hides)
                .Select(item => new PastTask(item.Task, item.SessionStart!.Value, item.DeviceName))
                .Concat(keptPast.Items.Where(item => !item.Task.IsDone && item.SessionStart is not null).Select(item => new PastTask(item.Task, item.SessionStart!.Value, item.SourceName)))
                .ToList();
        };
        receivedHistory.Changed += (_, _) => sessionTasks.NotifyListsChanged();
        keptPast.Changed += (_, _) => sessionTasks.NotifyListsChanged();
        pastSettled.Changed += (_, _) => sessionTasks.NotifyListsChanged();
        syncCoordinator.DecorateTaskLists = _taskListSync.Decorate;
        syncCoordinator.ReceiveTaskLists = _taskListSync.ReceiveAsync;
        // ADR 76: one task list for everything. Once (backup first, marker last) the old lists move into it; from then on the next
        // session's list, Later, come-backs, Auto-saved and categories are views of it, and sync reads and writes it (with the old
        // fields rebuilt from it for older apps). A failed or unbalanced migration leaves everything on the old lists.
        taskStore.SelfId = selfId;
        taskStore.OtherDevices = () => receivedDevices.Active.Select(device => (device.File.Device.DeviceId, device.File.Device.Kind)).ToList();
        _filterPresets = new AccountabiliBuddy.App.Services.TaskFilterPresetStore(documents, new DpapiDataProtector());
        var earlierTasksNotice = new AccountabiliBuddy.App.Services.Tasks.EarlierTasksNoticeStore(documents);
        AccountabiliBuddy.App.Services.Tasks.TaskModelState taskModel = AccountabiliBuddy.App.Services.Tasks.TaskModelMigration.RunBlocking(
            database, taskStore, sessionTasks, _later!, _comebacks!, autoSaved,
            _filterPresets, new TaskViewStore(documents), time, dataDirectory, earlierTasksNotice);
        earlierTasksNotice.LoadBlocking();
        syncCoordinator.TasksReady = () => taskModel != AccountabiliBuddy.App.Services.Tasks.TaskModelState.Unreadable;
        if (taskModel == AccountabiliBuddy.App.Services.Tasks.TaskModelState.Unreadable)
        {
            // The migration ran but its task list can't be read: the old documents stopped at the migration, so they are never the
            // source (deleted tasks would come back on every device and later edits be lost). The lists show empty views of the
            // closed task list (nothing is saved), aren't shared or taken in, and stay out of backups until the user rebuilds them.
            sessionTasks.UseTaskStore(taskStore);
            _later!.UseTaskStore(taskStore);
            _comebacks!.UseTaskStore(taskStore);
            autoSaved.UseTaskStore(taskStore);
            syncCoordinator.ShareTaskLists = () => false;
            backups.Tasks = null;
            // Text typed into them now (Ctrl+Alt+N, the work gate's come-back, the shutdown card) can't be kept: said, never dropped unseen.
            void ShowNotKept(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() => presenter.ShowNotice(
                "That wasn't saved",
                "Your task lists couldn't be opened, so new tasks can't be kept on this PC for now. Rebuild them first."));
            _comebacks!.AddRefused += ShowNotKept;
            autoSaved.AddRefused += ShowNotKept;
            _later!.AddRefused += ShowNotKept;
            Dispatcher.UIThread.Post(() => presenter.ShowChoice(
                "Your task lists couldn't be opened",
                "Tasks aren't shown, saved or synced on this PC for now. Rebuild them from the lists as they were when this version first "
                    + "started (a copy of everything is kept first; tasks changed since then on this PC aren't in it).",
                "Rebuild",
                () => _ = RebuildTaskListsAsync(database, documents, time, dataDirectory),
                () => { }));
        }

        if (taskModel == AccountabiliBuddy.App.Services.Tasks.TaskModelState.OldLists)
        {
            // The migration failed or didn't balance (details in tasks-model.log, counts only): the old lists stay the source.
            Dispatcher.UIThread.Post(() => presenter.ShowNotice(
                "Your task lists stayed as they were",
                "Moving them to the new task list didn't work, so nothing was changed. A copy of everything was kept first."));
        }

        // A ring that was cut off by a crash left the output raised: put the user's level back on every start, whatever the task model.
        var volumeGuard = new AccountabiliBuddy.App.Services.Tasks.MasterVolumeGuard(new AccountabiliBuddy.Platform.Windows.Audio.WindowsMasterVolume(), documents);
        volumeGuard.RestoreLeftoverBlocking();
        if (taskModel == AccountabiliBuddy.App.Services.Tasks.TaskModelState.TaskList)
        {
            sessionTasks.UseTaskStore(taskStore);
            _later!.UseTaskStore(taskStore);
            _comebacks!.UseTaskStore(taskStore);
            autoSaved.UseTaskStore(taskStore);
            _taskListSync.UsesTaskStore = () => true;
            // "End and mark the rest unfinished": the same end (wait and reason, armed or not); when it happens the open tasks go back to Queued.
            _endFlow!.MarkRestUnfinished = sessionTasks.MarkRestUnfinishedAtEndAsync;
            sessionArmedEnd.MarkRestUnfinished = sessionTasks.MarkRestUnfinishedAtEndAsync;
            syncCoordinator.TaskModelParts = taskStore.WithParts;
            syncCoordinator.MergeTaskModel = taskStore.MergeFileAsync;
            taskStore.Changed += (_, _) => syncCoordinator.NotifyHistoryChanged();
            // ADR 76: the task actions (undo, History, bulk), the Shelf's views and the weekly tidy-up: data only, the screens come later.
            _taskActions = new AccountabiliBuddy.App.Services.Tasks.TaskActionsService(taskStore, sessionTasks, time);
            _taskViews = new AccountabiliBuddy.App.Services.Tasks.TaskViewService(taskStore, taskSettings, taskAccount, time);
            _taskTidyUp = new AccountabiliBuddy.App.Services.Tasks.TaskTidyUpService(taskStore, _taskActions, taskSettings, _sessions, documents, time);
            _taskTidyUp.LoadBlocking();
            // ADR 76: task reminders (due times, snooze, Can't miss, ringing); the volume guard first puts back what a crashed ring left raised.
            // "During a focus session": Remind quietly is the looser direction (waits, refused in a session); the rest applies at once.
            var reminderSettings = new AccountabiliBuddy.App.Services.Tasks.TaskReminderSettingsService(
                documents, time, () => _sessions!.State.IsActive, () => _configuration!.Current.Locks.LooseningDelay);
            reminderSettings.LoadBlocking();
            _sessions!.Ticked += (_, _) => reminderSettings.ApplyPendingIfReady();
            _taskReminderSettings = reminderSettings;
            _taskReminders = new AccountabiliBuddy.App.Services.Tasks.TaskReminderService(taskStore, reminderSettings, documents, time) { Actions = _taskActions };
            _taskReminders.LoadBlocking();
            _taskReminderRinger = new AccountabiliBuddy.App.Services.Tasks.TaskReminderRinger(
                _taskReminders, reminderSettings, _sessionCues!, volumeGuard, () => _sessions!.State.IsActive, () => _sessionOverlay.Glow())
            {
                // "Start session" on a reminder card: the task waits in This session and the usual start controls open (no session starts by itself).
                OpenStart = () => _quickSession?.Show(),
            };
            _taskReminders.SessionRunning = () => _sessions!.State.IsActive;
            // A session starting holds normal reminders and its end queues the held ones (setting: During a focus session).
            _sessions!.StateChanged += (_, _) => _taskReminders.Refresh();
            _taskReminders.Start();
            // ADR 78: the tasks that ask for it go to the app's own Google calendar (title and times only), once the account allowed it.
            _taskCalendarExport = new AccountabiliBuddy.App.Services.Tasks.TaskCalendarExportService(
                taskStore, reminderSettings, calendars, new GoogleCalendarClient(_calendarNetwork, googleEndpoints, time),
                new GoogleCalendarWriter(_calendarNetwork, googleEndpoints), dataProtector, documents, time,
                Path.Combine(dataDirectory, AccountabiliBuddy.App.Services.Tasks.TaskCalendarExportService.LogFileName), taskAccount);
            _taskCalendarExport.LoadBlocking();
            _taskCalendarExport.SharesTaskLists = () => syncSettings.Current.ShareTaskLists;
            syncSettings.Changed += (_, _) => Dispatcher.UIThread.Post(() => _ = _taskCalendarExport.SyncNowAsync());
            _calendarSync.SkipCalendar = _taskCalendarExport.IsAppCalendar;
            // The default view and the picked calendar became the account's: this PC's own values are taken over once (where the account
            // has none), in memory before the export and the views start.
            _ = AdoptLegacyTaskSettingsAsync(taskAccount, taskSettings, reminderSettings);
            _taskCalendarExport.Start();
            _ = StartTaskViewsAsync();
        }

        // Round 2 R4 (brief §5.3): this PC's goals (synced with the other devices under ADR 79, below). Once (database backup first, marker last) the default
        // area "Work" and the built-in Deep work goal are made; Deep work's daily target stays the "goal" document (ADR 29), its weekly
        // target and "Track time only" live in "goals.deepWork". A failed first run leaves no marker and changes nothing else; goals that
        // can't be read stay closed (never rebuilt on a guess). Lowering Deep work follows the looser-change rule (GoalsService); guards
        // never read goal counts. No screen yet (R5).
        var goalStore = new AccountabiliBuddy.App.Services.Goals.GoalStore(documents, new DpapiDataProtector(), time);
        AccountabiliBuddy.App.Services.Goals.GoalsMigration.RunBlocking(database, goalStore, time, dataDirectory);
        _goals = new AccountabiliBuddy.App.Services.Goals.GoalsService(
            goalStore, goal, _sessions!, time, () => guidance.IsCompanion, goalLowerWaits, () => _configuration!.Current.Locks.LooseningDelay);
        guidance.Changed += (_, _) => Dispatcher.UIThread.Post(() => _ = _goals.ModeChangedAsync());
        // ADR 79 (GS3a): goals travel in the sealed sync files while this PC shares its task lists: every goal part changed here is stamped
        // with this PC's id, a received file's goals merge part by part, Deep work's received lower weekly target waits this PC's own
        // looser-change delay (never during a session outside PB), and a change here writes this PC's file again.
        goalStore.SelfId = () => selfId() ?? Guid.Empty;
        _goals.Self = goalStore.SelfId;
        var goalListSync = new AccountabiliBuddy.App.Services.Sync.GoalListSync(_goals, selfId, time);
        syncCoordinator.GoalParts = goalListSync.Decorate;
        syncCoordinator.MergeGoals = goalListSync.ReceiveAsync;
        goalStore.Changed += (_, _) => syncCoordinator.NotifyHistoryChanged();
        // ADR 84: a goal with a Beeminder source in direction Read counts that goal's datapoints day by day, read after each goals read
        // (the 30-minute schedule, Today and Goals opening) while a token is set; the entries travel like every goal entry (ADR 79) and only
        // a PC with a token changes or removes them.
        _beeminderDatapoints = new AccountabiliBuddy.App.Services.Beeminder.BeeminderDatapointsService(_calendarNetwork, goalStore, _beeminder!, time);
        _ = _beeminderDatapoints.ReconcileAsync();
        // ADR 71: the goals go in backups with the task lists, readable inside the file (notes and reasons only with a password).
        backups.Goals = AccountabiliBuddy.App.Services.Backup.BackupAccess.Goals(goalStore, _goals, () => _sessions!.IsEnforcing);
        // Round 2 R5: what the Goals page and Today's goals count from: this PC's counted sessions with their checklists (the day bar's
        // records and omitted-session rule), each other device's synced sessions, and the goals' own entries. The log is read again when
        // it is written; a received file, a goal change, the daily goal or a minute of focus only recompute. Never read by a guard.
        var goalData = new AccountabiliBuddy.App.Services.Goals.GoalSourceDataService(
            async (from, to) => sessionGaps.Apply(SessionHistoryBuilder.Build(await activityLog.GetSessionEventsAsync(from, to))),
            () => receivedDevices.Devices.Select(device => AccountabiliBuddy.App.Services.Sync.ReceivedSessions.Closed([device])),
            start => sessionTasks.ForSession(start) is { HasTasks: true } checklist ? sessionTasks.LabelledForSync(checklist) : null,
            omittedSessions.IsOmitted,
            () => _sessions!.State.IsRealSession,
            goalStore,
            time);
        // Milestone review S2 (keep CPU low): the log is written often while the PC is used; goal data is read again once things settle
        // (one read at most every 3 s, coalescing a burst of writes, as History does), not on every write.
        var goalLogSettled = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        goalLogSettled.Tick += (_, _) =>
        {
            goalLogSettled.Stop();
            _ = goalData.ReloadAsync();
        };
        _recorder!.LogChanged += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (!goalLogSettled.IsEnabled)
            {
                goalLogSettled.Start();
            }
        });
        receivedDevices.Changed += (_, _) => Dispatcher.UIThread.Post(goalData.Recompute);
        // ADR 79 (GS1b/GS3a): this PC's id (its own #tag and calendar extras aren't counted twice) and the phone's daily steps (a steps
        // goal counts the highest of them and a synced Health Connect value, never both).
        goalData.DeviceId = selfId;
        goalData.Steps = () => dailySteps.Current;
        dailySteps.Changed += (_, _) => Dispatcher.UIThread.Post(goalData.Recompute);
        // ADR 79 (GS3b): the derived day values this PC measures (#tag extras, calendar) go into its own goal entries, marked with its id,
        // for the other devices; at most every five minutes, and only when a value changed.
        var goalDerived = new AccountabiliBuddy.App.Services.Goals.GoalDerivedValuesService(goalStore, goalData, time) { InGroup = () => syncIdentity.Key is not null };
        goalData.Changed += (_, _) => goalDerived.Request();
        var goalDerivedTimer = new DispatcherTimer { Interval = AccountabiliBuddy.App.Services.Goals.GoalDerivedValuesService.MinGap };
        goalDerivedTimer.Tick += (_, _) => _ = goalDerived.RunAsync();
        goalDerivedTimer.Start();
        // ADR 79 (GS3b): the shared goal history, month and year files on Drive and the relay (sealed like the sync files, never in a QR
        // file); this PC's "merged year files" marker travels in its regular file. Counts only in goals-history.log.
        var goalHistoryState = new AccountabiliBuddy.App.Services.Sync.GoalHistoryStateStore(documents, new DpapiDataProtector());
        goalHistoryState.LoadBlocking();
        string goalHistoryLog = Path.Combine(dataDirectory, "goals-history.log");
        var goalHistory = new AccountabiliBuddy.App.Services.Sync.GoalHistorySync(
            _goals, goalListSync.Held, goalHistoryState, time, selfId, () => syncIdentity.Key,
            () => receivedDevices.Devices
                .Select(device => new AccountabiliBuddy.App.Services.Sync.GoalHistoryDeviceInfo(
                    device.File.Device.DeviceId, device.File.Device.Kind == AccountabiliBuddy.Core.Sync.SyncDeviceCard.KindPc,
                    receivedDevices.IsRemoved(device.File.Device.DeviceId), device.ReceivedAt, device.File.Device.Kind, device.File.Device.AppVersion))
                .ToList(),
            () => syncSettings.Current.ShareTaskLists,
            line =>
            {
                try
                {
                    File.AppendAllText(goalHistoryLog, $"{time.GetUtcNow():yyyy-MM-ddTHH:mm:ssZ} {line}{Environment.NewLine}");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The log is a help, never a reason to stop.
                }
            });
        goalListSync.YearsMerged = () => goalHistory.Marker;
        goalListSync.Merged = goalHistory.NoteRegularFile;
        goalHistory.MarkerChanged += (_, _) => syncCoordinator.NotifyHistoryChanged();
        goalHistory.MonthMerged = syncReceiptsStore.RecordHistory;
        syncCoordinator.GoalsReady = () => _goals?.Store.IsReady == true;
        _syncDrive!.GoalHistory = goalHistory;
        _syncRelay!.GoalHistory = goalHistory;
        _deepWork.Changed += (_, _) => Dispatcher.UIThread.Post(goalData.Recompute);
        goal.Changed += (_, _) => Dispatcher.UIThread.Post(goalData.Recompute);
        _ = goalData.ReloadAsync();
        // Round 2 R5.5 (brief §5.9): goal reminders ring through the task reminder engine (its queue, ringer, snooze, session rule and
        // volume guard) without becoming tasks; their state stays on this PC in the goals store. Only where the task list (and so the
        // engine) is in use. Never exported to Google Calendar (ADR 78 reads the task list only).
        var goalReminders = new AccountabiliBuddy.App.Services.Goals.GoalReminderSource(_goals, goalData, time);
        _goalReminders = goalReminders;
        _taskReminders?.AddSource(goalReminders);
        guidance.Changed += (_, _) => Dispatcher.UIThread.Post(goalReminders.NotifyModeChanged);
        // Round 2 R5.4: the weekly goals review (its day and time from Settings › Goals; never during a session; the tidy-up after it).
        var goalReview = new AccountabiliBuddy.App.Services.Goals.GoalReviewService(_goals, goalData, time, () => _sessions!.State.IsActive) { TidyUp = _taskTidyUp };
        _goalReview = goalReview;
        _sessions!.StateChanged += (_, _) => Dispatcher.UIThread.Post(goalReview.NotifyChanged);
        guidance.Changed += (_, _) => Dispatcher.UIThread.Post(goalReview.NotifyChanged);
        if (_taskTidyUp is { } reviewTidyUp)
        {
            reviewTidyUp.Changed += (_, _) => Dispatcher.UIThread.Post(goalReview.NotifyChanged);
        }

        goalReview.NotifyChanged();
        // Today's goals on the Today sidebar: the goals with Show on Today (R5 replaces R2a's Deep-work-only hook); Deep work's card keeps
        // the daily goal's edit and lines.
        sessionViewModel.TodayGoals = new TodayGoalsViewModel(sessionViewModel.Goal, _goals, goalData);
        // Q375: every older daily-goal screen (the goal card, the start-of-day card, the day plan and setup assistants) sets the goal through
        // the goals' one rule: raising at once; lowering waits the looser-change delay and never starts during a session outside PB.
        sessionViewModel.Goal.SetDailyGoal = minutes => SetDailyGoalAsync(goal, minutes);
        startOfDay.SetDailyGoal = minutes => SetDailyGoalAsync(goal, minutes);
        // R5.7 (plan §3b 7): Google Calendar goal sources count the time in events of a day plan category, as ADR 58 classifies them, from
        // the events this PC already keeps. Goal counts only: never focus totals, Stats or a guard. Rebuilt only when the calendars, the
        // classifications or the plan change.
        IReadOnlyDictionary<Guid, IReadOnlyList<LocalWindow>>? goalCalendar = null;
        void ForgetGoalCalendar()
        {
            goalCalendar = null;
            Dispatcher.UIThread.Post(goalData.Recompute);
        }

        goalData.CalendarWindows = () => goalCalendar ??= AccountabiliBuddy.App.Services.Goals.GoalCalendarWindows.From(
            calendars.AllEvents(), _configuration!.Current.DayPlan, calendarOverlay.Classifications, calendarOverlay.Rules.Active);
        calendars.Changed += (_, _) => ForgetGoalCalendar();
        calendarOverlay.Changed += (_, _) => ForgetGoalCalendar();
        _configuration.Changed += (_, _) => ForgetGoalCalendar();
        Func<Guid, string?> goalLabelName = labelId => sessionTasks.Categories.FirstOrDefault(category => category.Id == labelId)?.Name
            ?? sessionTasks.Categories.FirstOrDefault(category => AccountabiliBuddy.Core.Domain.TaskLabel.IdFor(category.Name) == labelId)?.Name;
        autoSaved.Changed += (_, _) => syncCoordinator.NotifyHistoryChanged();
        pastSettled.Changed += (_, _) => syncCoordinator.NotifyHistoryChanged();
        handovers.Changed += (_, _) => syncCoordinator.NotifyHistoryChanged();
        // "How did today go" (a feeling and a line): sealed on this PC, 90 days, shown over time in Stats.
        var shutdownReviews = new ShutdownReviewStore(documents, new DpapiDataProtector(), time);
        shutdownReviews.LoadBlocking();
        _shutdown = new ShutdownService(_sessions, _configuration, sessionTasks, _comebacks!, documents, autoSaved, shutdownReviews, calendars);
        _shutdown.LoadBlocking();
        // Work ended early (ADR 65): Start shutdown during a work part ends the block after the end-early wait and reason (StartShutdownFromUi).
        _workEnded = new WorkEndedService(_sessions, documents, new DpapiDataProtector());
        _armedEnds!.Register(new WorkEndArmedEndHandler(_workEnded, _sessions, () => _shutdown?.StartByHand() ?? false));
        _workEnded.LoadBlocking();
        sessionViewModel.StartShutdownHandler = () =>
        {
            StartShutdownFromUi();
            return true;
        };
        sessionViewModel.BackToWorkHandler = _workEnded.BackToWorkAsync;
        sessionViewModel.CanGoBackToWork = _workEnded.CanGoBack;
        _sessions.Ticked += (_, _) => sessionViewModel.CanGoBackToWork = _workEnded.CanGoBack;
        // Times only in this PC's sync file (never the reason); another PC's newer edit for today is taken, like lunch.
        syncCoordinator.WorkEnded = _workEnded.ForSync;
        // ADR 68: the chosen mode and its time; another device's newer choice is taken (ProductaBuddy after this PC's delay).
        syncCoordinator.Guidance = guidance.ForSync;
        guidance.Changed += (_, _) =>
        {
            // Round 2 review: the Today layout's lunch rule depends on the mode; the sidebar and Settings › Today read it again.
            Dispatcher.UIThread.Post(() => _todayLayout?.ModeChanged());
            _enforcement?.Refresh();
            _startNudge?.CheckNow();
            _preWork?.CheckNow();
            _workHours?.OnModeChanged();
            _runningWaits!.NotifyChanged();
            UpdateTray();
            syncCoordinator.NotifyHistoryChanged();
            _syncFolder?.WriteSoon();
        };
        // ADR 66: armed ends go out times only; a cancel from the phone comes back as an action. A change writes the file soon.
        syncCoordinator.ArmedEnds = () => _armedEnds!.Current.Select(end => new AccountabiliBuddy.Core.Sync.SyncArmedEnd(end.Id, SyncKindOf(end.Kind), end.ReadyAt)).ToList();
        _armedEnds!.Changed += (_, _) =>
        {
            syncCoordinator.NotifyHistoryChanged();
            _syncFolder?.WriteSoon();
        };
        receivedDevices.Changed += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            foreach (var device in receivedDevices.Active.Where(device => device.File.Guidance is not null))
            {
                guidance.Receive(device.File.Guidance!);
            }

            foreach (var device in receivedDevices.Active.Where(device => device.File.WorkEnded is not null))
            {
                // ADR 67: a phone's finished work is a looser change from another device: it waits this PC's looser-change delay.
                if (device.File.Device.Kind == AccountabiliBuddy.Core.Sync.SyncDeviceCard.KindPhone)
                {
                    _workEnded.ReceiveFromPhone(device.File.WorkEnded!, _configuration.Current.Locks.LooseningDelay);
                }
                else
                {
                    _workEnded.Receive(device.File.WorkEnded!);
                }
            }
        });
        _workEnded.Changed += (_, _) =>
        {
            sessionViewModel.CanGoBackToWork = _workEnded.CanGoBack;
            _dayBar?.Redraw();
            timelineDay.Redraw();
            syncCoordinator.NotifyHistoryChanged();
            _syncFolder?.WriteSoon();
        };
        _shutdown.IsDayOff = () => _preWork.IsDayOff;
        _shutdown.Changed += (_, _) =>
        {
            sessionViewModel.Shutdown = _shutdown.Current;
            if (_shutdown.Current is { StartedByHand: false } && !_shutdown.CardWasRestored)
            {
                presenter.ShowNotice("Work's done for today", "A short shutdown routine is waiting on Today: capture, look ahead, plan tomorrow.");
            }
        };
        // "Work over for today": Today says so, the start nudges stay quiet and a thought parked now is for tomorrow.
        _shutdown.WorkOverChanged += (_, _) => ShowWorkOver(sessionViewModel);
        ShowWorkOver(sessionViewModel);
        _capture!.IsWorkOver = () => _shutdown.IsWorkOver;
        // The reminder before work ends (the service holds it back during a session and over an open card).
        _shutdown.ReminderDue += (_, reminder) => presenter.ShowReminderWithAction(
            $"Work ends at {reminder.WorkEndsAt.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture)}",
            "A short shutdown routine is ready when you are.",
            "Start shutdown",
            StartShutdownFromUi);
        _shutdown.AutoSaved += (_, _) => presenter.ShowNotice("Wrap-up closed", "Your tomorrow task is saved under Tasks → Auto-saved.");
        // Earned time (design K): today's goal met across the user's devices opens this computer's work hours.
        _workHours!.Goal = () => _deepWork?.UnlockProgress;
        // Counted focus per date for the day plan's Budget days: one source for every guard that reads the work windows (ADR 47).
        Func<DateOnly, TimeSpan> countedFocus = date => _deepWork?.FocusedOn(date) ?? TimeSpan.Zero;
        _workHours.FocusedOn = countedFocus;
        _startNudge!.FocusedOn = countedFocus;
        _headphones!.FocusedOn = countedFocus;
        _shutdown.FocusedOn = countedFocus;
        _dayBar.FocusedOn = countedFocus;
        timelineDay.FocusedOn = countedFocus;
        // A Budget day whose plan has no Sleep part takes its night from the sleep settings.
        Func<DateOnly, SleepTimes?> nightFallback = date => sleepSettings.Current.FallbackNightBefore(date);
        _workHours.NightFallback = nightFallback;
        // What the fallback will be once a plan is saved (SleepSettingsService.SaveFromPlanAsync): the plan's own typical nights.
        _workHours.NightFallbackFor = plan =>
        {
            SleepSettings now = sleepSettings.Current;
            SleepSettings derived = now with
            {
                SameEveryNight = false,
                Weeknights = DayPlanSleep.TypicalNight(plan, workday: true) ?? DayPlanSleep.SleepOf(plan.Workday, plan) ?? now.Weeknights,
                Weekends = DayPlanSleep.TypicalNight(plan, workday: false) ?? DayPlanSleep.SleepOf(plan.OtherDay, plan) ?? now.Weekends,
            };
            return date => derived.FallbackNightBefore(date);
        };
        _startNudge.NightFallback = nightFallback;
        effectivePlan.NightFallback = nightFallback;
        effectivePlan.CountedFocus = () => _deepWork?.CountedWindows ?? Array.Empty<LocalWindow>();
        // A plan edit's freed work hours are measured with the events laid over both plans (ADR 28, 58).
        _workHours.WithCalendarEvents = effectivePlan.Of;
        // The guards read the plan with the day's classified calendar events laid over it (ADR 58).
        Func<DayPlan> guardedPlan = () => effectivePlan.Current;
        // Work ended early (ADR 65): one list, cut out of the work windows by DayWorkWindows for every guard.
        Func<IReadOnlyList<LocalWindow>> workEnded = () => _workEnded.Windows;
        _workHours.WorkEnded = workEnded;
        _startNudge.WorkEnded = workEnded;
        _shutdown.WorkEnded = workEnded;
        _dayBar.WorkEnded = workEnded;
        timelineDay.WorkEnded = workEnded;
        _headphones!.WorkEnded = workEnded;
        _workEnded.WorkIn = (range, now) => DayWorkWindows.WorkWindows(effectivePlan.Current, range, now, countedFocus, nightFallback, _workEnded.Windows)
            .Select(focus => focus.Window)
            .ToList();
        _workHours.GuardedPlan = guardedPlan;
        // ADR 68: in ProductaBuddy these stay quiet (their Cold Turkey holds don't change).
        Func<bool> isCompanion = () => guidance.IsCompanion;
        _workHours.IsCompanion = isCompanion;
        _startNudge.IsCompanion = isCompanion;
        _preWork.IsCompanion = isCompanion;
        _bedtimeReminder!.IsCompanion = isCompanion;
        _startNudge.GuardedPlan = guardedPlan;
        _shutdown.GuardedPlan = guardedPlan;
        _preWork.GuardedPlan = guardedPlan;
        _dayBar.GuardedPlan = guardedPlan;
        timelineDay.GuardedPlan = guardedPlan;
        _headphones!.NightFallback = nightFallback;
        _shutdown.NightFallback = nightFallback;
        WatchPowerTransitions(monitorLocator);
        _dayBar.NightFallback = nightFallback;
        timelineDay.NightFallback = nightFallback;
        _ = _deepWork.ReloadAsync();
        sessionViewModel.Review = _sessionReview!.Pending;
        _sessionReview.Changed += (_, _) =>
        {
            sessionViewModel.Review = _sessionReview.Pending;
            if (_sessionReview.Pending is { SessionStart: DateTimeOffset reviewed } card)
            {
                card.PhoneText = _phonePickups?.Describe(reviewed) ?? string.Empty;
            }
        };
        sessionViewModel.Focus = new FocusViewModel(
            _sessions, _configuration, _stepAway, sessionViewModel.Tasks, _dayBar, () => _endFlow.TryEndSessionAsync(VisibleMainWindow()))
        {
            PlanFirst = _planFirst,
            TimerToggle = sessionViewModel.TimerToggle,
            PhoneLine = () => sessionTasks.Current.SessionStart is DateTimeOffset started ? _phonePickups!.Describe(started) : null,
        };
        // The away card ("Back to <task>?", a side task's length): the task in front, the end-early flow, the session's chime.
        _stepAway!.CurrentTaskName = () =>
        {
            string task = sessionViewModel.Tasks?.CurrentTaskText ?? string.Empty;
            return task == "All tasks done" ? string.Empty : task; // Nothing left to go back to: "Back to your session?".
        };
        _stepAway.EndSessionAsync = () => _endFlow!.TryEndSessionAsync(null);
        _stepAway.PlaySideTaskChime = () => _sessionCues?.Play(soundSettings.Current.FocusEndSound);
        // The planning "Ready?" card shares the task line; it waits while the away card is up, so two never stack.
        _planFirst!.CurrentTaskName = _stepAway.CurrentTaskName;
        _stepAway.CardChanged += (_, _) =>
        {
            ShowAwayCard(monitorLocator);
            ShowReadyCard(monitorLocator);
            ShowLunchRunIntoCard(monitorLocator);
        };
        _planFirst.CardChanged += (_, _) =>
        {
            ShowReadyCard(monitorLocator);
            ShowLunchRunIntoCard(monitorLocator);
        };
        // Capture in Focus mode (redesign phase 5): notes for the session's review, come-backs with ⚑ or "!".
        sessionViewModel.Focus.Capture = new CaptureBoxViewModel(_sessionNotes!, _comebacks!);
        var notebook = new NotebookViewModel(_comebacks!, _sessionNotes!, sessionTasks, time);
        // The Plan page (design 5o): the editors (day plan, month view) are sheets over it; the helper remembers what "Leave it" dismissed.
        var calendarFeeds = new CalendarFeedsViewModel(calendars, _calendarSync, _sessions, _configuration, effectivePlan, OpenInBrowser);
        DayPlanViewModel dayPlanViewModel = WithSleepGoal(new DayPlanViewModel(_configuration, _sessions) { Today = () => DateOnly.FromDateTime(time.GetLocalNow().DateTime) }, sleepSettings);
        var exceptionsViewModel = new ExceptionsViewModel(_configuration, _sessions, calendars, calendarFeeds, pendingChanges);
        var planHelperDismissals = new PlanHelperDismissals(documents, () => DateOnly.FromDateTime(time.GetLocalNow().DateTime));
        planHelperDismissals.LoadBlocking();
        var planViewModel = new PlanViewModel(
            _configuration, _sessions, calendars, dayPlanViewModel, exceptionsViewModel, planHelperDismissals,
            () => TimeSpan.FromMinutes(goal.Current.DailyMinutes), countedFocus, nightFallback, effectivePlan);
        _deepWork.Changed += (_, _) => Dispatcher.UIThread.Post(planViewModel.NotifyFocusChanged);
        planViewModel.WorkEnded = workEnded;
        _workEnded.Changed += (_, _) => planViewModel.NotifyWorkEndedChanged();
        var historyViewModel = new HistoryViewModel(activityLog, _recorder, _configuration, privacy, omittedSessions, displayNames, _icons, hiddenUsage, hiddenDistractions, deletedUsage, time,
            _programPaths, desktopShell, isSessionRunning: () => _sessions!.State.IsRealSession)
        {
            ReviewService = _sessionReview,
            Gaps = sessionGaps,
            NotesBetween = _sessionNotes.Between,
        };
        // The Stats page (design 5p, 19a to 19c): the same history, added up as views; History itself is its "Day details".
        var statsViewModel = new StatsViewModel(
            historyViewModel, activityLog, _recorder,
            async (from, to) => sessionGaps.Apply(SessionHistoryBuilder.Build(await activityLog.GetSessionEventsAsync(from, to))),
            receivedDevices, omittedSessions, deletedUsage, displayNames, goal, _configuration, sleepSettings, _phonePickups!, dailySteps, privacy, documents, time,
            () => _sessions!.State.IsRealSession, shutdownReviews);
        // The shutdown routine's review step: today's focus against the goal and the time off track (history on only), the same numbers as Stats.
        _shutdown.FocusFor = date =>
        {
            if (_deepWork is null)
            {
                return null;
            }

            return date == DateOnly.FromDateTime(time.GetLocalNow().DateTime)
                ? _deepWork.Progress
                : new AccountabiliBuddy.Core.Engine.GoalProgress(_deepWork.ShownFocusOn(date), goal.Current.DailyMinutes);
        };
        _shutdown.DriftFor = async date =>
        {
            if (!privacy.Current.ActivityLogEnabled)
            {
                return null;
            }

            TimeZoneInfo zone = time.LocalTimeZone;
            DateTime dayStart = date.ToDateTime(TimeOnly.MinValue);
            DateTime dayEnd = dayStart.AddDays(1);
            var from = new DateTimeOffset(dayStart, zone.GetUtcOffset(dayStart));
            var to = new DateTimeOffset(dayEnd, zone.GetUtcOffset(dayEnd));
            DateTimeOffset now = time.GetUtcNow();
            IReadOnlyList<SessionRecord> records = sessionGaps.Apply(SessionHistoryBuilder.Build(await activityLog.GetSessionEventsAsync(from.AddDays(-1), to.AddDays(1))));
            List<TimeRange> leftOut = records
                .Where(record => omittedSessions.IsOmitted(record.StartedAt) || record.Mode == SessionMode.Scheduled)
                .Select(record => new TimeRange(record.StartedAt, record.EndedAt ?? now))
                .ToList();
            IReadOnlyList<DriftStretch> visits = DriftMarks.Merge(await activityLog.GetDriftAsync(from, to, leftOut, deletedUsage.Parts), TimeSpan.FromMinutes(2));
            return visits.Aggregate(TimeSpan.Zero, (sum, visit) => sum + (visit.End - visit.Start));
        };
        var sessionLayout = new AccountabiliBuddy.App.Services.Tasks.SessionLayoutService(_sessions!, time);
        _sessionLayout = sessionLayout;
        // How the Tasks page and Today's task sections are laid out on this PC (density, split, folds, pins).
        var taskLayout = new AccountabiliBuddy.App.Services.Tasks.TaskLayoutStore(documents);
        taskLayout.LoadBlocking();
        // Round 2: the Today page's layout (sidebar parts, Panel / Chip / Off, in and out of a session). Outside ProductaBuddy the lunch
        // part can't be off.
        var todayLayout = new AccountabiliBuddy.App.Services.Today.TodayLayoutService(documents, () => AppBrand.Name == AppBrand.CompanionName);
        todayLayout.LoadBlocking();
        _todayLayout = todayLayout;
        // The app's name follows the mode (ADR 68): its change also redraws the Today layout (the lunch rule reads it).
        AppBrand.Changed += (_, _) => Dispatcher.UIThread.Post(todayLayout.ModeChanged);
        var mainViewModel = new MainWindowViewModel(
            sessionViewModel,
            new ProfilesViewModel(_configuration, _sessions, pendingChanges, overlaySettings, _coldTurkey, receivedDevices, exemptionDefaults),
            dayPlanViewModel,
            exceptionsViewModel,
            historyViewModel,
            setupViewModel,
            new IntegrationsViewModel(new ColdTurkeyDetector(), new WindowsOtherBlockerDetector(), sightings, _configuration, _sessions) { Beeminder = beeminderViewModel, CalendarFeeds = calendarFeeds },
            new SettingsViewModel(
                _configuration, _sessions, _quickSession, calendars, dataProtector, pendingChanges, dayBarSettings, overlaySettings, soundSettings, _sessionCues, sleepSettings, database.DatabasePath,
                _noise)
            {
                StepAway = _stepAway,
                OpenStartOver = openStartOver,
                WorkHours = new WorkHoursViewModel(_workHours!, _sessions, _armedEnds!),
                Hyperfocus = new HyperfocusViewModel(_hyperfocus!, _configuration),
                StartNudge = new StartNudgeViewModel(_startNudge!),
                Lunch = new LunchSettingsViewModel(_lunch!),
                Shutdown = new ShutdownSettingsViewModel(_shutdown!),
                Motion = new MotionViewModel(_motion!),
                Appearance = new AppearanceViewModel(appTheme),
                Headphones = new HeadphoneSettingsViewModel(_headphones!),
                LightsOut = new LightsOutViewModel(_lightsOut!),
                Backup = new BackupViewModel(backups, _sessions, RestartApp, showExport: true),
                BackupCards = _autoBackup is null
                    ? []
                    : new[] { BackupKind.SyncedSettings, BackupKind.DeviceSettings, BackupKind.TaskLists }
                        .Select(kind => new BackupKindCardViewModel(kind, backups, _autoBackup, driveFiles, syncIdentity, syncSettings, _sessions, RestartApp, time))
                        .ToList(),
                Updates = new UpdatesViewModel(_updates),
                EnforcementLimit = enforcementLimit,
                ComputerTypes = computerTypes,
                PrivacyChoices = privacy,
                GoogleGuide = new GoogleSetupGuide(calendars, _calendarSync, dataProtector, documents, OpenInBrowser),
                PcAlarmWarning = () => _pcAlarm!.WakeProblem(),
                Reminders = new BedtimeRemindersViewModel(sleepSettings, () => _bedtimeReminder!.DeviceId()),
                // ADR 35: only a linked phone measures sleep.
                HasLinkedPhone = () => receivedDevices.Active.Any(device => device.File.Device.Kind == AccountabiliBuddy.Core.Sync.SyncDeviceCard.KindPhone),
            },
            activityViewModel,
            Environment.GetEnvironmentVariable(DevToolsVariable) == "1",
            _configuration.LoadError)
        {
            Tasks = new TasksViewModel(sessionTasks, _later!, _comebacks!, autoSaved, notebook, sessionViewModel, _configuration!, time, taskLayout)
            {
                // ADR 72: "Keep on…" offers this PC and the group's other PCs and phones (never the watch).
                SelfId = selfId,
                SetLocal = _taskListSync!.SetLocalAsync,
                KeepOnDevices = () => receivedDevices.Active
                    .Where(device => device.File.Device.Kind is AccountabiliBuddy.Core.Sync.SyncDeviceCard.KindPc or AccountabiliBuddy.Core.Sync.SyncDeviceCard.KindPhone)
                    .Where(device => device.File.Device.DeviceId != syncIdentity.DeviceId)
                    .Select(device => (device.File.Device.DeviceId, device.File.Device.Name))
                    .ToList(),
                // "Changed on both devices" names the device each version came from.
                DeviceOf = deviceId => receivedDevices.Active.FirstOrDefault(device => device.File.Device.DeviceId == deviceId) is { } known
                    ? (receivedDevices.LabelOf(known.File.Device), known.File.Device.Kind)
                    : null,
            },
            Plan = planViewModel,
            Stats = statsViewModel,
            DayBar = _dayBar,
            Search = new SearchViewModel(SearchDataAsync),
            Devices = devices,
            // The title bar's light/dark toggle and theme menu (round 2): through AppTheme, this PC's "theme" document only.
            ThemeToggle = new ThemeToggleViewModel(appTheme),
            // The title bar's session control: Start opens the usual start controls (the quick-session popup, as the global
            // shortcut does); End is the end-early flow with its wait and reason.
            SessionControl = new SessionControlViewModel(
                _sessions!, _stepAway!, sessionLayout, () => _quickSession!.Show(), () => _endFlow!.TryEndSessionAsync(VisibleMainWindow()))
            {
                EndToQueued = taskModel == AccountabiliBuddy.App.Services.Tasks.TaskModelState.TaskList
                    ? () => _endFlow!.TryEndSessionAsync(VisibleMainWindow(), unfinishedToQueued: true)
                    : null,
                OffersEndToQueued = () => sessionTasks.CanEndToQueued,
            },
        };
        // Round 2 R5: the Goals page (after Stats in the rail). Stats can't filter by goal yet, so its button opens Stats; the dialogs
        // (New goal, Edit goal, areas, the weekly review) and Settings › Goals are R5b's (hooks).
        var goalsPage = new GoalsViewModel(_goals, goalData, () => guidance.IsCompanion, goalLabelName)
        {
            OpenGoalsSettings = () => mainViewModel.OpenSettings(SettingsSection.Goals),
            SessionPresets = () => _configuration!.Current.Profiles
                .Select(profile => new GoalSessionPreset(profile.Id, profile.Name, profile.Id == _quickSession!.CountUpProfileId))
                .ToList(),
            // The usual start (QuickSessionService: the remembered length, the session rules); refused while one runs.
            StartPreset = profileId => Task.FromResult<string?>(_sessions!.State.IsActive
                ? "A session is already running."
                : _quickSession!.StartQuick(profileId) ? null : "The session couldn't start (its profile or the session rules held it)."),
            OpenStartControls = () => _quickSession!.Show(),
            SessionRunning = () => _sessions!.State.IsActive,
            // Opens Beeminder's site in the user's browser: the app itself makes no connection for it.
            OpenInBrowser = address => OpenInBrowser(new Uri(address)),
        };
        // Stats for the goal shown: its year with paused stretches hatched over Stats' own views (R5.4); Stats alone without a goal.
        // Month lines (HM1): Stats shows the goals store's one choice and saves through it; every open heatmap follows at once.
        statsViewModel.MonthLines = _goals.Store.PageSettings.MonthLines;
        statsViewModel.SetMonthLines = lines => _goals.SetPageSettingsAsync(_goals.Store.PageSettings with { MonthLines = lines }, statsViewModel);
        _goals.Store.Changed += (_, _) => Dispatcher.UIThread.Post(() => statsViewModel.MonthLines = _goals.Store.PageSettings.MonthLines);
        goalsPage.OpenStats = goalId =>
        {
            if (goalId is Guid shownId && goalsPage.Facts.FirstOrDefault(facts => facts.Goal.Id == shownId) is { } shownFacts)
            {
                string? areaName = _goals.Lists.Areas.FirstOrDefault(area => area.Id == shownFacts.Goal.AreaId && area.IsLive)?.Name;
                statsViewModel.ShowGoal(new StatsGoalHistoryViewModel(shownFacts, goalData.Current, _goals.Store.PageSettings, areaName));
            }

            mainViewModel.SelectedTab = MainWindowViewModel.StatsTab;
        };
        goalsPage.NewGoalRequested += async (_, _) => await ShowGoalEditorAsync(goalData, null, GoalEditSection.Basics, false, sessionTasks, guidance);
        goalsPage.EditGoalRequested += async (_, request) => await ShowGoalEditorAsync(goalData, request.GoalId, request.Section, request.FocusActive, sessionTasks, guidance);
        goalsPage.EditAreaRequested += async (_, areaId) =>
        {
            if (_goals.Lists.Areas.FirstOrDefault(area => area.Id == areaId && area.IsLive) is { } editedArea)
            {
                await GoalAreaDialog.ShowAsync(VisibleMainWindow(), new GoalAreaEditorViewModel(_goals, editedArea));
            }
        };
        goalsPage.AreasRequested += async (_, _) => await GoalAreasDialog.ShowAsync(VisibleMainWindow(), _goals);
        // The This week card's review row: Start opens the weekly goals review (then the tidy-up when it follows), Snooze per mode.
        goalsPage.Review = goalReview;
        goalReview.Changed += (_, _) => goalsPage.MarkChanged();
        goalsPage.ReviewRequested += async (_, _) => await ShowGoalReviewAsync(goalReview, goalData, sessionTasks, guidance, taskStore, time);
        goalsPage.ReviewSnoozeRequested += async (_, _) => await goalReview.SnoozeAsync();
        // A goal reminder's Log a value… / Add time… / Open goal: the Goals page with the goal shown (today's day panel on Edit to log).
        goalReminders.OpenGoal = (goalId, log) =>
        {
            ShowMainWindow();
            mainViewModel.SelectedTab = MainWindowViewModel.GoalsTab;
            if (log)
            {
                goalsPage.LogToday(goalId);
            }
            else
            {
                goalsPage.Select(goalId);
            }
        };
        mainViewModel.Goals = goalsPage;
        // Today's goals on the sidebar (board B-PC-Idle): the tick circle and Log run the goal menu's own quick-log actions; "Goals ›" opens the page.
        if (sessionViewModel.TodayGoals is { } todayGoals)
        {
            todayGoals.Tick = id => goalsPage.TickTodayAsync(id);
            todayGoals.Log = id =>
            {
                mainViewModel.SelectedTab = MainWindowViewModel.GoalsTab;
                goalsPage.LogToday(id);
            };
            todayGoals.OpenGoals = () => mainViewModel.SelectedTab = MainWindowViewModel.GoalsTab;
        }
        if (_goals.Store.IsReady)
        {
            mainViewModel.Settings.GoalsPage = new GoalsSettingsViewModel(_goals)
            {
                OpenAreas = () => _ = GoalAreasDialog.ShowAsync(VisibleMainWindow(), _goals),
                OpenIntegrations = () => mainViewModel.OpenSettings(SettingsSection.Connections),
            };
        }

        // Settings › Goals › "Run the tidy-up straight after": the tidy-up moves to the review's day and its own card stops showing.
        if (_taskTidyUp is { } tidyUp)
        {
            tidyUp.FollowsReviewOn = () => _goals.Store.PageSettings is { ReviewSettings.TidyUpAfter: true } goalSettings ? goalSettings.ReviewDay : null;
            _goals.Store.Changed += (_, _) => tidyUp.NotifyChanged();
        }
        guidance.Changed += (_, _) => Dispatcher.UIThread.Post(goalsPage.MarkChanged);
        // "Ended · switches next time": opening Tasks again switches it back to planning; header clicks on its sections are forgotten.
        mainViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.SelectedTab) && mainViewModel.SelectedTab == (int)MainTab.Tasks)
            {
                sessionLayout.TasksOpened();
                mainViewModel.Tasks?.TasksOpened();
            }
            else if (args.PropertyName == nameof(MainWindowViewModel.SelectedTab))
            {
                // Round 2 R3: a Shelf shown during a session hides again once the user leaves Tasks.
                mainViewModel.Tasks?.TasksLeft();
            }
        };
        if (mainViewModel.Tasks is { } tasksPage)
        {
            // Today shows the same task sections; a row opened there opens on the Tasks page (it has the detail card).
            sessionViewModel.TaskPage = tasksPage;
            tasksPage.OpenRequested += (_, _) => mainViewModel.SelectedTab = (int)MainTab.Tasks;
            // The Tasks page's Today strip (round 2 R3): Open Today shows the Today tab.
            tasksPage.OpenTodayRequested += (_, _) => mainViewModel.SelectedTab = (int)MainTab.Today;
            // The Tasks and Today headers' Options menus end with "Tasks settings" / "Today settings" (round 2, brief §3.3): Settings at
            // that section, behind Settings' own session locks.
            tasksPage.OpenSettings = mainViewModel.OpenSettings;
            // Today's Options › Arrange cards… / Sidebar items and order…: Settings › Today scrolled to that part (round 2).
            tasksPage.OpenSettingsArea = (section, area) =>
            {
                mainViewModel.OpenSettings(section);
                mainViewModel.Settings.Select(section, area);
            };
            // Today's list follows Settings › Today (Show finished today).
            tasksPage.UseTodayLayout(todayLayout);
            // Rows say "untouched N wk" after the Tasks setting's weeks; the open task's sync line reads the title bar's sync light.
            tasksPage.UntouchedWeeks = () => _taskSettings?.Current.UntouchedWeeks ?? AccountabiliBuddy.Core.Tasks.TaskUntouched.DefaultWeeks;
            tasksPage.SyncLight = () => mainViewModel.SyncLight;
            // The add box's title help reads the user's project markers and offers to learn new ones.
            tasksPage.TaskSettings = _taskSettings;
            // "Add new reminders to": a task's calendar switch when the Reminders & due date menu gives it its first reminder or due date.
            tasksPage.ReminderSettings = _taskReminderSettings;
            if (_taskActions is not null && _taskViews is not null && _taskTidyUp is not null && _taskReminders is not null)
            {
                tasksPage.UseTaskModel(taskStore, _taskActions, _taskViews, _taskTidyUp, _taskReminders);
                // "Work on it now" with This session full: the same "This session is full" dialog (swap or Add anyway).
                _taskReminders.SessionFull += (_, taskId) => Dispatcher.UIThread.Post(() => tasksPage.OfferSwapFor(taskId, _taskReminderRinger?.DialogOwner));
            }

            // The session layout (Compact, the Shelf hidden, faded Queued), the ended notice and Hold to reset (Tasks redesign 3.6).
            tasksPage.UseSessionLayout(sessionLayout);

            // Settings › Tasks: only while the task list is the source (its reminder settings, views and calendar export exist then).
            if (_taskSettings is not null && _taskReminderSettings is not null)
            {
                // The Labels screen (Settings › Tasks › Labels) and Alerts › Task reminders' "ring on": the devices are the Devices & sync list's.
                TaskSettingsViewModel? taskSettingsPage = null;
                LabelsViewModel? labelsScreen = null;
                AccountabiliBuddy.App.Services.Tasks.TaskLabelService? labelService = null;
                IReadOnlyList<AccountabiliBuddy.App.Services.Tasks.TaskRingDevice> RingDevices() => TaskRingDeviceList.From(devices.Rows, syncIdentity.DeviceId);
                if (taskStore.IsActive)
                {
                    labelService = new AccountabiliBuddy.App.Services.Tasks.TaskLabelService(taskStore, sessionTasks);
                    labelsScreen = new LabelsViewModel(labelService, RingDevices)
                    {
                        Toasts = tasksPage.Toasts,
                        Close = () => taskSettingsPage?.CloseLabels(),
                    };
                }

                taskSettingsPage = new TaskSettingsViewModel(
                    _taskSettings, _taskReminderSettings, _taskCalendarExport, _taskViews, _taskAccount, time)
                {
                    OpenSection = mainViewModel.OpenSettings,
                    OpenSectionArea = (section, area) =>
                    {
                        mainViewModel.SelectedTab = MainWindowViewModel.SettingsTab;
                        mainViewModel.Settings.Select(section, area);
                    },
                    OpenBrowser = OpenInBrowser,
                    PomodoroFocusMinutes = () => (int)_configuration!.Current.PomodoroDefaults.FocusLength.TotalMinutes,
                    StartSessionKeys = () => _quickSession?.Preferences.Hotkey ?? string.Empty,
                    LooserWait = () => _configuration!.Current.Locks.LooseningDelay,
                    LabelsScreen = labelsScreen,
                    // "Start from another copy": a backup file from this PC, the account's copy or a fresh start (owner, 2026-11-11).
                    ViewChooser = taskStore.IsActive && _taskAccount is not null ? new TaskViewChooserViewModel(_taskAccount, _taskViews, backups.Read, time) : null,
                };
                mainViewModel.Settings.TasksPage = taskSettingsPage;
                if (labelsScreen is not null && labelService is not null && _taskReminders is not null && _taskAccount is not null)
                {
                    // Every "Manage labels" entry (the Shelf chips' +, the View menu's Labels ›, Settings › Tasks › Labels) opens the screen.
                    void OpenLabelsScreen()
                    {
                        mainViewModel.OpenSettings(SettingsSection.Tasks);
                        taskSettingsPage.OpenLabels();
                    }

                    tasksPage.Shelf.OpenLabels = OpenLabelsScreen;
                    var reminderAlerts = new TaskReminderAlertsViewModel(_taskReminderSettings, _taskReminders, _taskAccount, labelService, RingDevices)
                    {
                        OpenLabels = OpenLabelsScreen,
                        OpenDevices = () => mainViewModel.OpenSettings(SettingsSection.DevicesAndSync),
                        Clock = time,
                        LooserWait = () => _configuration!.Current.Locks.LooseningDelay,
                    };
                    mainViewModel.Settings.TaskReminderAlerts = reminderAlerts;
                    _taskReminderAlerts = reminderAlerts;
                    devices.RowsRefreshed += (_, _) =>
                    {
                        reminderAlerts.Load();
                        labelsScreen.Refresh();
                        // "Which devices should ring?" applies once a phone is in the list.
                        _setupFeatures?.Refresh();
                    };
                }
            }
        }
        planViewModel.OpenDpAssistant = mainViewModel.OpenDpAssistantCommand;
        planViewModel.ShowToday = () => mainViewModel.SelectedTab = MainWindowViewModel.TodayTab;
        // The menu on a calendar event, wherever it shows, and the same changes asked from a phone (ADR 64).
        var eventActions = new CalendarEventActions(effectivePlan, calendars, _later!);
        var eventMenu = new CalendarEventMenu(effectivePlan, _configuration!, calendars, eventActions, time, message => presenter.ShowNotice("Calendar event", message));
        planViewModel.EventMenu = eventMenu;
        _dayBar.EventMenu = eventMenu;
        timelineDay.EventMenu = eventMenu; // Today's timeline is this bar.
        syncActions.EventActions = eventActions;
        syncActions.CancelArmed = id => _armedEnds!.Cancel(id);
        exceptionsViewModel.PickEventCategory = planViewModel.OpenEventPicker;
        exceptionsViewModel.OpenConnections = () =>
        {
            planViewModel.CloseSheet();
            mainViewModel.OpenSettings(SettingsSection.Connections);
        };
        // Beeminder (ADR 43): every 30 minutes, and again when Today opens with an answer a few minutes old.
        mainViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.SelectedTab) && mainViewModel.SelectedTab is (int)MainTab.Today or (int)MainTab.Goals)
            {
                _beeminder!.RefreshIfStale();
            }
        };
        _beeminder!.Start();
        // The top-right refresh: every sync method that's on, calendars, then the day bar and History read again.
        mainViewModel.RefreshAll = async () =>
        {
            var problems = new List<string>();
            DateTimeOffset passStart = time.GetUtcNow();
            async Task Step(string name, Func<Task>? step)
            {
                if (step is null)
                {
                    return;
                }

                try
                {
                    await step();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or InvalidOperationException)
                {
                    problems.Add($"{name}: {ex.Message}");
                }
            }

            // A method that is on but whose write failed, so the tip doesn't claim a send that didn't happen.
            var failedSends = new List<string>();
            Func<Task>? Exchange(string method, Func<Task<bool>>? write, Func<Task>? read)
            {
                if (write is null || read is null)
                {
                    return null;
                }

                return async () =>
                {
                    bool wrote = await write();
                    if (!wrote && AccountabiliBuddy.App.Services.Sync.SyncReachText.MethodsOn(syncSettings.Current).Contains(method))
                    {
                        failedSends.Add(method);
                    }

                    await read();
                };
            }

            await Step("sync folder", Exchange(AccountabiliBuddy.Core.Sync.SyncDeviceCard.MethodFolder, _syncFolder is null ? null : _syncFolder.WriteNowAsync, _syncFolder is null ? null : _syncFolder.ReadNowAsync));
            await Step("Google Drive", Exchange(AccountabiliBuddy.Core.Sync.SyncDeviceCard.MethodDrive, _syncDrive is null ? null : _syncDrive.WriteNowAsync, _syncDrive is null ? null : _syncDrive.ReadNowAsync));
            await Step("own relay", Exchange(AccountabiliBuddy.Core.Sync.SyncDeviceCard.MethodRelay, _syncRelay is null ? null : _syncRelay.WriteNowAsync, _syncRelay is null ? null : _syncRelay.ReadNowAsync));
            await Step("calendars", _calendarSync is null ? null : _calendarSync.RefreshAllAsync);
            _dayBar?.ReloadHistory();
            await mainViewModel.History.RefreshCommand.ExecuteAsync(null);
            DateTimeOffset localNow = time.GetLocalNow();
            string at = AccountabiliBuddy.App.Services.Sync.SyncReachText.Clock(localNow);
            if (problems.Count > 0)
            {
                return $"Refreshed {at}, but: " + string.Join("; ", problems);
            }

            // In a group: say who was reached (or that nobody was and why) instead of "Up to date".
            return syncIdentity.Key is null
                ? $"Up to date · refreshed {at}"
                : $"Refreshed {at}. " + AccountabiliBuddy.App.Services.Sync.SyncReachText.SyncNowLine(
                    syncSettings.Current, receivedDevices.Active, failedSends, passStart, time.GetUtcNow(), time.LocalTimeZone);
        };

        // A session added, edited or deleted by hand (ADR 20): both day bars read the history again, and other devices get it.
        DayBarViewModel addedSessionsBar = _dayBar;
        mainViewModel.History.AddedSessionsChanged += (_, _) =>
        {
            addedSessionsBar.ReloadHistory();
            timelineDay.ReloadHistory();
            syncCoordinator.NotifyHistoryChanged();
        };
        mainViewModel.History.UseReceived(receivedDevices);

        // The QR code method (ADR 22): today's codes from a button (top bar, Ctrl+Alt details) and an end-of-day reminder.
        var qrSync = new AccountabiliBuddy.App.Services.Sync.QrSyncService(
            syncCoordinator, syncIdentity, syncSettings, receivedDevices, _configuration, documents, time);
        qrSync.LoadBlocking();
        qrSync.FocusedOn = _workHours!.FocusedOn;
        qrSync.NightFallback = _workHours.NightFallback;
        qrSync.WorkEnded = _workHours.WorkEnded;
        // One end-of-day prompt at a time: the shutdown card goes first, the scan reminder follows once it is answered.
        qrSync.Deferred = () => _shutdown!.IsCardOpenOrDue();
        _qrSync = qrSync;
        var syncImport = new AccountabiliBuddy.App.Services.Sync.SyncImportService(syncCoordinator, syncIdentity);
        void ShowSyncQr() => new SyncQrWindow { DataContext = new SyncQrViewModel(qrSync.LinksForNowAsync) }.Show();
        void ShowQrButtons(AccountabiliBuddy.App.Services.Sync.SyncSettings settings)
        {
            mainViewModel.ShowsQrButton = settings.QrOn;
            _sessionOverlay!.SetSyncQr(settings.QrOn ? ShowSyncQr : null);
        }

        mainViewModel.ShowSyncQr = ShowSyncQr;

        // ADR 37: this PC's Cold Turkey blocks go in its sync file; "Plan a day" lists every device's blocks.
        IReadOnlyList<AccountabiliBuddy.Core.Sync.SyncBlocker>? OwnBlockers() =>
            _coldTurkey!.Blocks?.Blocks.Select(block => new AccountabiliBuddy.Core.Sync.SyncBlocker(
                BlockRef.ColdTurkeyBlock, block.Name, block.Name, block.IsSwitchedOn, block.IsLockedAt(time.GetLocalNow().DateTime))).ToList();
        syncCoordinator.Blockers = OwnBlockers;

        // ADR 39: AB holds the keys to the Cold Turkey blocks the user assigns, during its windows; ADR 37 re-arm.
        string? coldTurkeyFolder = new ColdTurkeyDetector().Detect().InstallFolder;
        var coldTurkeyKeys = new ColdTurkeyKeysService(
            _coldTurkey!, new ColdTurkeyCommandLine(() => coldTurkeyFolder), _sessions!,
            () => _preWork!.IsGuarding, () => _workHours!.IsGuarding, () => effectivePlan.Current,
            () => syncIdentity.DeviceId == Guid.Empty ? null : syncIdentity.DeviceId,
            documents, new DpapiDataProtector(), time);
        coldTurkeyKeys.LoadBlocking();
        coldTurkeyKeys.FocusedOn = _workHours!.FocusedOn;
        coldTurkeyKeys.NightFallback = _workHours.NightFallback;
        coldTurkeyKeys.WorkEnded = _workHours.WorkEnded;
        _workEnded!.Changed += (_, _) => coldTurkeyKeys.CheckSoon();
        _coldTurkeyKeys = coldTurkeyKeys;
        _runningWaits!.AddSource(() => coldTurkeyKeys.NewKeyReadyAt is DateTimeOffset keyAt
            ? [new RunningWait("spareKey", "New spare key", keyAt, coldTurkeyKeys.CancelNewKeyAsync)]
            : []);
        coldTurkeyKeys.Notice += (_, notice) => presenter.ShowNotice(notice.Title, notice.Text);
        // A new key comes from a timer, a check or a session ending, never straight from a click: shown without taking focus.
        coldTurkeyKeys.SpareKeyNeeded += (_, _) => ShowColdTurkeyKey(ColdTurkeyKeyMode.SpareKey, quiet: true);
        _workHours!.Changed += (_, _) => coldTurkeyKeys.CheckSoon();
        _coldTurkey!.Changed += (_, _) => coldTurkeyKeys.CheckSoon();
        coldTurkeyKeys.Start();

        // ADR 59: the quit safeguard, off by default.
        var quitWatchdog = new AccountabiliBuddy.Platform.Windows.Startup.ProcessQuitWatchdog(
            Program.InstanceName, Environment.ProcessPath!, StartupOptions.WatchdogArgument);
        var quitGuard = new QuitGuardService(
            documents, time, _sessions!, coldTurkeyKeys, quitWatchdog, () => _configuration!.Current.Locks.LooseningDelay, () => effectivePlan.Current,
            () =>
            {
                // Never for a build that isn't the installed app (dotnet run, a test copy).
                if (AppPaths.IsInstalled && !startupRegistrar.IsEnabled)
                {
                    startupRegistrar.Enable();
                }
            });
        quitGuard.LoadBlocking();
        // ADR 71: restoring this PC's settings changes the quit guard and PC-off through their services, so an old backup can't
        // loosen them at once (a looser part waits; leaving "All the time" needs the spare key).
        // Round 2: Today's layout goes through its service, so the page redraws and the copy in memory isn't saved over it.
        if (_todayLayout is { } restoredTodayLayout)
        {
            backups.DeviceRestorers[AccountabiliBuddy.App.Services.Today.TodayLayoutService.DocumentKey] = restoredTodayLayout.RestoreAsync;
        }

        backups.DeviceRestorers["quit.guard"] = async value =>
        {
            if (System.Text.Json.JsonSerializer.Deserialize<QuitGuardSettings>(value, AccountabiliBuddy.Core.Sync.SyncJson.Options) is not { } wanted)
            {
                return null;
            }

            return await quitGuard.ChangeAsync(wanted) switch
            {
                QuitGuardChange.Waiting => "The quit guard's looser parts take effect after the countdown.",
                QuitGuardChange.NeedsSpareKey or QuitGuardChange.WrongKey => "The quit guard wasn't restored: leaving \"All the time\" needs your spare key. Change it in Settings.",
                QuitGuardChange.NeedsSpareKeySetup => "The quit guard wasn't restored: \"All the time\" needs a spare key first. Set it up in Settings.",
                QuitGuardChange.RefusedInFocus => "The quit guard wasn't restored during a focus session.",
                _ => null,
            };
        };
        // ADR 76: the Tasks settings go through their service too: more actions per session than now waits like any looser change
        // (refused during a focus session), and a waiting change in the backup is never taken over.
        foreach (GuidanceMode tasksMode in new[] { GuidanceMode.Guarding, GuidanceMode.Companion })
        {
            backups.DeviceRestorers[ModeSettings.KeyFor(AccountabiliBuddy.App.Services.Tasks.TaskSettingsService.DocumentKey, tasksMode)] = async value =>
            {
                if (_taskSettings is null
                    || System.Text.Json.JsonSerializer.Deserialize<AccountabiliBuddy.App.Services.Tasks.TaskSettings>(value, AccountabiliBuddy.Core.Sync.SyncJson.Options) is not { } wanted)
                {
                    return null;
                }

                string? note = await _taskSettings.RestoreAsync(wanted, tasksMode);
                // An older backup's default view: the account's now, restored like a newer backup's (it wins over the account's current choice).
                if (wanted.DefaultViewId is Guid oldDefaultView && _taskAccount is not null)
                {
                    await _taskAccount.SetDefaultViewAsync(oldDefaultView);
                }

                return note;
            };
        }

        // ADR 76, 78: the task reminder settings go through their service, so the ringer and the calendar export follow at once (a ring
        // setting loosens nothing about a session); "During a focus session" follows its own rule (Remind quietly waits, refused in a
        // session) and a waiting change in the backup is never taken over. An older backup's picked calendar is the account's now, taken
        // over only where it has none.
        backups.DeviceRestorers[AccountabiliBuddy.App.Services.Tasks.TaskReminderSettingsService.DocumentKey] = async value =>
        {
            if (_taskReminderSettings is null
                || System.Text.Json.JsonSerializer.Deserialize<AccountabiliBuddy.App.Services.Tasks.TaskReminderSettings>(
                    value, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)) is not { } wanted)
            {
                return null;
            }

            if (wanted.PickedCalendarId is { } oldPicked && _taskAccount is not null)
            {
                await _taskAccount.AdoptLegacyAsync(null, oldPicked);
                wanted = wanted with { PickedCalendarId = null };
            }

            await _taskReminderSettings.UpdateAsync(_ => wanted);
            if (!await _taskReminderSettings.SetDuringSessionAsync(wanted.DuringSession))
            {
                return "Task reminders' \"During a focus session\" wasn't restored during a focus session.";
            }

            return _taskReminderSettings.Current.PendingDuringSession is not null && wanted.DuringSession != _taskReminderSettings.Current.DuringSession
                ? "Task reminders' \"Remind quietly\" takes effect after the countdown."
                : null;
        };

        // Round 2 R4: the goals page's settings go through their store (display preferences as they are) and Deep work's weekly target
        // through its rules (a lower one, none or Track time only waits like a change made here). The sealed goal lists that the first
        // goals build put in device backups are still taken in, through the store (opened with this PC's key, never written raw).
        if (_goals is { } restoredGoals)
        {
            foreach (string goalsKey in AccountabiliBuddy.App.Services.Goals.GoalStore.SealedKeys)
            {
                backups.DeviceRestorers[goalsKey] = value => restoredGoals.Store.RestoreListAsync(goalsKey, value);
            }

            foreach (string goalsKey in new[] { AccountabiliBuddy.App.Services.Goals.GoalStore.UiKey, AccountabiliBuddy.App.Services.Goals.GoalStore.SettingsKey })
            {
                backups.DeviceRestorers[goalsKey] = value => restoredGoals.Store.RestoreDisplayAsync(goalsKey, value);
            }

            backups.DeviceRestorers[AccountabiliBuddy.App.Services.Goals.GoalStore.DeepWorkKey] = restoredGoals.RestoreDeepWorkAsync;
        }

        // ADR 76: the task store keeps these two in memory and saves them back, so a raw restore would be overwritten: it goes through the store.
        foreach (string mapKey in new[] { AccountabiliBuddy.App.Services.Tasks.TaskStore.CategoryIdsKey, AccountabiliBuddy.App.Services.Tasks.TaskStore.ViewSearchesKey })
        {
            backups.DeviceRestorers[mapKey] = value => taskStore.RestoreDeviceMapAsync(mapKey, value);
        }

        backups.DeviceRestorers["session.pcOff"] = async value =>
        {
            if (System.Text.Json.JsonSerializer.Deserialize<PcOffSettings>(value, AccountabiliBuddy.Core.Sync.SyncJson.Options) is not { } wanted)
            {
                return null;
            }

            await pcOff.SetTellMeAsync(wanted.TellMe);
            await pcOff.SetKeepOriginalEndAsync(wanted.KeepOriginalEnd);
            return pcOff.KeepEndPendingAt is not null ? "Keeping the planned end when the PC goes off takes effect after the countdown." : null;
        };
        quitGuard.FocusedOn = _workHours!.FocusedOn;
        quitGuard.NightFallback = _workHours.NightFallback;
        quitGuard.WorkEnded = _workHours.WorkEnded;
        _quitGuard = quitGuard;
        setupViewModel.StartupLocked = () => quitGuard.Current.On;
        quitGuard.Changed += (_, _) => setupViewModel.RefreshStartupLock();
        _quitGuardViewModel = new QuitGuardViewModel(quitGuard, _sessions!) { ShowSpareKey = () => ShowColdTurkeyKey(ColdTurkeyKeyMode.SpareKey) };
        _configuration!.Changed += (_, _) => _quitGuardViewModel.Refresh();
        mainViewModel.QuitGuard = _quitGuardViewModel;
        mainViewModel.Settings.PcOffPage = new PcOffSettingsViewModel(_pcOff!, _sessions!);
        var guidanceSwitch = new GuidanceSwitchFlow(
            _guidance!, _sessions!, _armedEnds!, () => _configuration!.Current.Locks.LooseningDelay, () => _mainWindow);
        _guidanceViewModel = new GuidanceModeViewModel(_guidance!, _sessions!) { SwitchToCompanion = guidanceSwitch.SwitchToCompanionAsync };
        mainViewModel.Settings.GuidancePage = _guidanceViewModel;
        // ADR 69: Settings shows the active mode's per-mode values, and hides AccountabiliBuddy-only rows in ProductaBuddy.
        mainViewModel.Settings.IsCompanionNow = () => _guidance!.IsCompanion;
        modeSettings.Reload += (_, _) => mainViewModel.Settings.ReloadForMode();
        mainViewModel.Settings.ReloadForMode();
        if (_modeSettings is not null)
        {
            _guidanceViewModel.Compare = new ModeCompareViewModel(_modeSettings, _guidance!);
            _ = _guidanceViewModel.Compare.LoadAsync();
        }
        _guidanceViewModel.ArmedSwitchAt = () => _armedEnds?.Find(ArmedEndKind.Companion)?.ReadyAt;
        _guidanceViewModel.CancelArmedSwitch = () =>
        {
            if (_armedEnds?.Find(ArmedEndKind.Companion) is ArmedEnd armed)
            {
                _armedEnds.Cancel(armed.Id);
            }
        };
        quitGuard.Start();

        // The migration assistant: features added since the last version that aren't set up yet.
        var setupFeatures = new AccountabiliBuddy.App.Services.Setup.SetupFeaturesService(
            documents, () => AccountabiliBuddy.App.Services.Setup.SetupFeatures.All(coldTurkeyKeys, _coldTurkey!, syncSettings, _configuration, _startNudge!, dpAssistant, appTheme, sessionTasks, planHelperDismissals, overlaySettings, calendarOverlay, quitGuard, _shutdown!, _guidance!,
                taskStore, taskAccount, _taskCalendarExport, _taskReminderSettings, () => _taskReminderAlerts?.HasPhones == true, _goals, todayLayout), BackupService.AppVersion);
        setupFeatures.LoadBlocking();
        _setupFeatures = setupFeatures;
        coldTurkeyKeys.Changed += (_, _) => setupFeatures.Refresh();
        appTheme.Changed += (_, _) => setupFeatures.Refresh();
        sessionTasks.Changed += (_, _) => setupFeatures.Refresh();
        syncSettings.Changed += (_, _) => setupFeatures.Refresh();
        quitGuard.Changed += (_, _) => setupFeatures.Refresh();
        _guidance!.Changed += (_, _) => setupFeatures.Refresh();
        _coldTurkey!.Changed += (_, _) => setupFeatures.Refresh();
        // TD1: the Today entry is set up once the one-time steps card is dealt with.
        todayLayout.Changed += (_, _) => Dispatcher.UIThread.Post(setupFeatures.Refresh);
        // Round 2 R5: the Goals entry is set up once the weekly review's day is chosen (Settings › Goals or the assistant).
        if (_goals is { } featureGoals)
        {
            featureGoals.Store.Changed += (_, _) => Dispatcher.UIThread.Post(setupFeatures.Refresh);
        }

        // The Tasks redesign's features (Phase 8): a choice made on another device sets them up here too.
        taskAccount.Changed += (_, _) => Dispatcher.UIThread.Post(setupFeatures.Refresh);
        if (_taskCalendarExport is not null)
        {
            _taskCalendarExport.StatusChanged += (_, _) => Dispatcher.UIThread.Post(setupFeatures.Refresh);
        }

        if (_taskReminderSettings is not null)
        {
            _taskReminderSettings.Changed += (_, _) => Dispatcher.UIThread.Post(setupFeatures.Refresh);
        }

        // The store raises this on any thread, inside a lock: only post.
        calendarOverlay.Changed += (_, _) => Dispatcher.UIThread.Post(setupFeatures.Refresh);
        sessionViewModel.WhatsNew = new WhatsNewCardViewModel(setupFeatures, ShowWhatsNew);
        // MIG1: one card, shown on Today and on Tasks, never during a session; Sort them now opens Tasks (To sort is the Shelf's first column).
        var earlierTasksCard = new EarlierTasksCardViewModel(earlierTasksNotice, () => sessionViewModel.IsSessionActive, () => mainViewModel.SelectedTab = (int)MainTab.Tasks);
        sessionViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SessionViewModel.IsSessionActive))
            {
                earlierTasksCard.Refresh();
            }
        };
        sessionViewModel.EarlierTasks = earlierTasksCard;
        if (mainViewModel.Tasks is not null)
        {
            mainViewModel.Tasks.EarlierTasks = earlierTasksCard;
        }

        sessionViewModel.NextUp = new NextUpViewModel(_configuration, time, sessionViewModel.Day, sessionTasks) { GuardedPlan = guardedPlan };
        sessionViewModel.CalendarToday = new CalendarTodayViewModel(_configuration, effectivePlan, calendars, time, sessionViewModel.Day);
        sessionViewModel.SeenWhilePlanning = new SeenWhilePlanningViewModel(_planFirst!, displayNames.NameFor);
        sessionViewModel.MoreToday = new MoreTodayViewModel(sessionViewModel, todayLayout);
        // TD1: which of the session card and This session are drawn, and the one-time "Steps now live in Goals" card (never auto-creates).
        sessionViewModel.MainColumn = new TodayMainColumnViewModel(sessionViewModel, () => sessionViewModel.IsSessionActive, todayLayout);
        if (_goals is { } stepsGoals)
        {
            sessionViewModel.StepsMoved = _stepsMovedCard = new StepsMovedCardViewModel(todayLayout, dailySteps, stepsGoals);
        }

        // Round 2: the Today sidebar (Next up, Today's goals, Come back to, chips) over This session's list, and Settings › Today.
        // Created once the cards it shows exist (Next up and the calendar lines are made just above).
        sessionViewModel.Sidebar = new TodaySidebarViewModel(
            sessionViewModel, todayLayout,
            new AccountabiliBuddy.App.Services.Today.TodaySidebarState(todayLayout.Current.SidebarInSession, todayLayout.Current.SidebarOutside))
        {
            OpenSettings = () =>
            {
                mainViewModel.OpenSettings(SettingsSection.Today);
                mainViewModel.Settings.Select(SettingsSection.Today, PageOptionsMenus.TodaySidebarArea);
            },
            OpenPlan = () => mainViewModel.SelectedTab = MainWindowViewModel.PlanTab,
        };
        mainViewModel.Settings.TodayPage = new TodaySettingsViewModel(todayLayout) { OpenSection = mainViewModel.OpenSettings };
        mainViewModel.Settings.Updates.OpenWhatsNew = ShowWhatsNew;
        mainViewModel.Settings.Updates.ShowEmergencyRelease = () => ShowColdTurkeyKey(ColdTurkeyKeyMode.Emergency);
        mainViewModel.Settings.Updates.ShowNewSpareKey = () => ShowColdTurkeyKey(ColdTurkeyKeyMode.NewKey);
        mainViewModel.Integrations.ShowNewSpareKey = () => ShowColdTurkeyKey(ColdTurkeyKeyMode.NewKey);
        IReadOnlyList<(BlockRef Block, string Device, bool On)> PlanDayBlocks()
        {
            _coldTurkey!.RefreshNow();
            var blocks = new List<(BlockRef Block, string Device, bool On)>();
            if (syncIdentity.DeviceId != Guid.Empty)
            {
                string me = BlockRef.Pc(syncIdentity.DeviceId);
                blocks.AddRange((OwnBlockers() ?? Array.Empty<AccountabiliBuddy.Core.Sync.SyncBlocker>())
                    .Select(b => (new BlockRef(me, b.Kind, b.Id, b.Name), "This PC", b.On)));
            }

            foreach (var device in receivedDevices.Active.Where(d => d.File.Blockers is not null))
            {
                var card = device.File.Device;
                string who = card.Kind == AccountabiliBuddy.Core.Sync.SyncDeviceCard.KindPhone ? BlockRef.Phone : BlockRef.Pc(card.DeviceId);
                blocks.AddRange(device.File.Blockers!.Select(b => (new BlockRef(who, b.Kind, b.Id, b.Name), card.Name, b.On)));
            }

            return blocks;
        }

        void ShowPlanDay()
        {
            var viewModel = new PlanDayViewModel(_configuration!, _sessions!, PlanDayBlocks, DateOnly.FromDateTime(time.GetLocalNow().DateTime), () => sleepSettings.PhoneAlarm);
            new PlanDayWindow { DataContext = viewModel }.Show();
        }

        // The Setup DP Assistant (Day plan tab): one window at a time; also opened from the migration assistant.
        DpAssistantWindow? dpAssistantWindow = null;
        void ShowDpAssistant()
        {
            if (dpAssistantWindow is not null)
            {
                dpAssistantWindow.Activate();
                return;
            }

            var viewModel = new DpAssistantViewModel(_configuration!, sleepSettings, goal, _sessions!, dpAssistant, _lunch!)
            {
                Today = () => DateOnly.FromDateTime(time.GetLocalNow().DateTime),
                GoalLowerWaits = goalLowerWaits,
                SetDailyGoal = minutes => SetDailyGoalAsync(goal, minutes),
                WorkHoursGuardOn = () => _workHours?.Settings.On == true,
                HasLinkedPhone = () => receivedDevices.Active.Any(device => device.File.Device.Kind == AccountabiliBuddy.Core.Sync.SyncDeviceCard.KindPhone),
                RefreshProgress = () => _deepWork?.CheckNow(),
            };
            dpAssistantWindow = new DpAssistantWindow { DataContext = viewModel };
            dpAssistantWindow.Closed += (_, _) => dpAssistantWindow = null;
            dpAssistantWindow.Show();
            dpAssistantWindow.Activate();
        }

        _showDpAssistant = ShowDpAssistant;
        _showCalendarRules = () =>
        {
            ShowMainWindow();
            mainViewModel.OpenSettings(SettingsSection.Connections);
        };
        mainViewModel.ShowDpAssistant = ShowDpAssistant;
        sessionViewModel.ShowPlanDay = ShowPlanDay;
        mainViewModel.DayPlan.ShowPlanDay = ShowPlanDay;
        devices.ShowSyncQr = ShowSyncQr;
        devices.Import = syncImport;
        devices.DriveAccount = driveAccount;
        devices.Relay = _syncRelay;
        devices.RelayHost = new RelayHostViewModel(_relayHost!, syncSettings, new WindowsNetworkTrust());
        devices.OpenBrowser = OpenInBrowser;
        // Troubleshoot sync (SYNC1b): the board, the unreadable files, re-read and re-send, the conflicts list, the report. Display and maintenance
        // only (nothing locked in a session except answering the day plan question).
        var troubleshootService = new AccountabiliBuddy.App.Services.Sync.SyncTroubleshootService(
            syncCoordinator, syncIdentity, syncSettings, receivedDevices, _syncFolder!, _syncDrive!, _syncRelay!, time);
        devices.Troubleshoot = new SyncTroubleshootViewModel(
            troubleshootService, syncCoordinator, receivedDevices, _sessions, time,
            new AccountabiliBuddy.App.Services.Sync.TaskConflictSource(
                taskStore, () => _taskActions, deviceId => receivedDevices.Active.FirstOrDefault(device => device.File.Device.DeviceId == deviceId) is { } known
                    ? (receivedDevices.LabelOf(known.File.Device), known.File.Device.Kind)
                    : null, selfId, time))
        {
            Receipts = new SyncReceiptsViewModel(syncReceipts),
            AnswerClash = request => ShowDayPlanClashDialog(syncCoordinator, request, time),
        };
        devices.SyncNow = async () =>
        {
            // Writes, then reads the other devices' files through the same accept path as a normal read; says which writes failed.
            IReadOnlyList<string> on = AccountabiliBuddy.App.Services.Sync.SyncReachText.MethodsOn(syncSettings.Current);
            var failed = new List<string>();

            // One method at a time, so one that throws doesn't skip the others. A write that didn't happen on a method that is
            // on is a failed send; a read that throws is the method's own problem line, and the report says only who answered.
            async Task Exchange(string method, Func<Task<bool>> write, Func<Task> read)
            {
                bool wrote = false;
                try
                {
                    wrote = await write();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
                {
                }

                if (!wrote && on.Contains(method))
                {
                    failed.Add(method);
                }

                try
                {
                    await read();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
                {
                }
            }

            await Exchange(AccountabiliBuddy.Core.Sync.SyncDeviceCard.MethodFolder, _syncFolder!.WriteNowAsync, _syncFolder.ReadNowAsync);
            await Exchange(AccountabiliBuddy.Core.Sync.SyncDeviceCard.MethodDrive, _syncDrive!.WriteNowAsync, _syncDrive.ReadNowAsync);
            await Exchange(AccountabiliBuddy.Core.Sync.SyncDeviceCard.MethodRelay, _syncRelay!.WriteNowAsync, _syncRelay.ReadNowAsync);

            // Seals this PC's file again.
            syncCoordinator.NotifyHistoryChanged();
            return failed;
        };
        ShowQrButtons(syncSettings.Current);
        syncSettings.Changed += (_, settings) => ShowQrButtons(settings);
        qrSync.ReminderDue += (_, _) => presenter.ShowWithLater(
            "Scan today's code to sync your phone",
            "Your phone and watch get today's focus from this PC's QR codes.",
            "Show code",
            ShowSyncQr,
            minutes => _ = qrSync.SnoozeAsync(minutes));

        sessionViewModel.NoisePlayer = noisePlayer;
        mainViewModel.Settings.OpenDayPlan = () => mainViewModel.SelectedTab = MainWindowViewModel.PlanTab;
        _shutdown.OpenPlan = () => mainViewModel.SelectedTab = MainWindowViewModel.PlanTab;
        _mainWindow = new MainWindow { DataContext = mainViewModel };
        // The start nudge: Today, with the keyboard in "Add a task". After a session (or a calendar event, lunch or the
        // met goal) the user may be typing elsewhere, so the window stays where it is and only its taskbar button flashes.
        _startNudge!.Due += (_, reason) =>
        {
            mainViewModel.SelectedTab = (int)MainTab.Today;
            if (reason is StartNudgeReason.SessionEnded or StartNudgeReason.Resumed)
            {
                ShowMainWindowQuietly();
                return;
            }

            ShowMainWindow();
            sessionViewModel.QuickStart.Checklist?.RequestFocus();
        };
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
        // Turning sleep mode off waits the same way (and not during a session).
        _sessions.Ticked += (_, _) => _ = sleepSettings.ApplyPendingOffIfDueAsync();
        _sessions.StateChanged += (_, _) => _ = sleepSettings.ApplyPendingOffIfDueAsync();
        _sessions.Ticked += (_, _) =>
        {
            _ = pcOff.ApplyPendingIfDueAsync();
            ClearStaleGoingOff();
            WriteSessionHeartbeat();
        };
        _sessions.StateChanged += (_, _) => _ = pcOff.ApplyPendingIfDueAsync();
        // ADR 68: a switch to ProductaBuddy applies once its wait is over and no session runs.
        _sessions.Ticked += (_, _) => _ = _guidance!.TickAsync();
        _sessions.StateChanged += (_, _) => _ = _guidance!.TickAsync();
        _sessions.SessionGapHandled += (_, report) => ShowSessionGap(report, presenter);

        _recorder.Start();
        _sessions.Start(savedSession);
        // After the saved session is back, so an armed end of it is kept rather than dropped as gone.
        _armedEnds.LoadBlocking();
        // Until now the saved session wasn't put back, so the calendar overlay treated a session as running (nothing loosens).
        effectivePlan.MarkSessionStateKnown();
        // An erase the last run could not finish (a focus session was running) is done now, after a crashed session is resumed so
        // that it still waits for that one.
        _ = _syncRemoval!.ApplyPendingErasesQuietlyAsync();
        _ = sessionTasks.AfterStartupAsync();
        _icons.Start(_activityWatcher, _browserSource);
        _programPaths.Start(_activityWatcher);
        _updates.Start();
        _activityWatcher.Start();
        _browserSource.Start();
        _calendarSync.Start();
        _clockAnchors.Start();
        // ADR 70: network time anchors the trusted clock (start, every 6 hours, on waking).
        _networkTime = new NetworkTimeService(time, _calendarNetwork!);
        _networkTime.Start();
        _autoBackup?.Start(_appUpdated);
        _sessionCues.Start();
        // What the overlay and its Ctrl+Alt panel ask the app: the same commands and gates as the Today page.
        _sessions!.SessionCompleted += (_, finished) =>
        {
            // A phase that ran out while the PC was off isn't "done" (ADR 60).
            if (!_sessions.IsCatchingUp && finished.IsRealSession && finished.Phase == Core.Domain.SessionPhase.Focus && finished.PhaseStartedAt is { } began && finished.PhaseEndsAt is { } ended)
            {
                presenter.ShowSessionDone($"{(int)(ended - began).TotalMinutes} min done", _deepWork is { Progress.Met: true } ? "Today's goal is met." : "A good moment for a break.");
            }
        };
        presenter.DriftChanged += (_, label) => _sessionOverlay.SetDrift(label, _sessions!.Now);
        presenter.OverlayAnchor = () => _sessionOverlay.TileAnchor;
        presenter.ReminderExpired = _sessionOverlay.ShowNudgeDot;
        OverlayActions overlayActions = _sessionOverlay.Actions;
        overlayActions.OpenApp = ShowMainWindow;
        overlayActions.Notify = presenter.ShowNotice;
        overlayActions.OpenSettings = () =>
        {
            ShowMainWindow();
            mainViewModel.OpenSettings(SettingsSection.AlertsAndOnScreen);
        };
        overlayActions.EndSession = () => _endFlow!.TryEndSessionAsync(VisibleMainWindow());
        overlayActions.ExtendFocus = () => _sessions!.ExtendFocus(TimeSpan.FromMinutes(10));
        overlayActions.ParkThought = () => _capture!.Show();
        overlayActions.ShowStart = () => _quickSession!.Show();
        overlayActions.StartDefault = () => _quickSession!.StartWithDefaultAsync();
        overlayActions.CanStartLunch = () => _lunch is { CanStart: true };
        overlayActions.StartLunch = () => _lunch!.StartAsync();
        overlayActions.IsWorkDone = () => _deepWork is { Progress.Met: true };
        overlayActions.ComebackTitles = () => _comebacks?.Open.OrderByDescending(item => item.CreatedAt).Select(item => item.Text).Take(4).ToList() ?? [];
        overlayActions.NextUp = () => sessionViewModel.NextUp?.Rows.Take(3).Select(row => (row.TimeText, row.Title)).ToList() ?? [];
        overlayActions.OpenTrayOverflow = _taskbarGeometry!.InvokeTrayOverflow;
        overlayActions.OpenCalendarFlyout = _taskbarGeometry.InvokeClock;
        overlayActions.NextMeeting = () =>
        {
            DateTime local = TimeZoneInfo.ConvertTime(_sessions!.Now, _sessions.TimeProvider.LocalTimeZone).DateTime;
            CalendarEntry? next = calendars.EventsOn(DateOnly.FromDateTime(local))
                .Where(entry => !entry.Event.IsAllDay && entry.Event.LocalStart > local)
                .OrderBy(entry => entry.Event.LocalStart).FirstOrDefault();
            return next is null ? null : (next.Event.Title, next.Event.LocalStart);
        };
        _sessionOverlay.Start();
        mainViewModel.Settings.OverlayPage = new OverlaySettingsViewModel(
            overlaySettings, _sessions, _sessionOverlay.TurnOn, () => _sessionOverlay.StickyHotkeyError, () => _sessionOverlay.HideHotkeyError);
        _timers!.Start();
        _bedtimeReminder.ReminderDue += (_, reminder) =>
            presenter.ShowReminder(reminder.Title, reminder.Message, new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#6366F1")));
        // ADR 38: a reminder that rings opens the alarm window; this PC's sync id says which reminders are its own.
        _ = new BedtimeAlarmRinger(_bedtimeReminder, _sessionCues, () => _sessionOverlay.Glow());
        // ADR 68: when ProductaBuddy takes effect, bedtime reminders ringing or shown here stop (wake-up alarms and timers don't).
        modeSettings.Reload += (_, _) =>
        {
            if (_guidance!.IsCompanion)
            {
                _bedtimeReminder.StopAllForCompanion();
                presenter.CloseReminders();
            }
        };
        _bedtimeReminder.Start();
        _coldTurkey.ExpectedBlocksOff += (_, names) => presenter.ShowNotice("Cold Turkey block off", ColdTurkeyService.Describe(names));
        _coldTurkey.Start();
        syncActions.EndedFromDevice += (_, reason) => presenter.ShowNotice("Session ended from your phone", reason);
        syncCoordinator.ClashRaised += (_, request) => ShowDayPlanClash(syncCoordinator, request, presenter, time);
        _syncFolder.Start();
        _syncDrive.Start();
        _syncRelay.Start();
        _relayHost!.Start();
        _qrSync?.Start();
        _activityWatcher.ActivityChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(_sessionOverlay.OnActivityChanged);
        _dayBar.Start();
        // The main window's bar redraws only while the window is open; the on-screen timeline has its own copy.
        MainWindow mainWindow = _mainWindow;
        DayBarViewModel mainDayBar = _dayBar;
        StatsViewModel? stats = mainViewModel.Stats;
        mainWindow.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.IsVisibleProperty)
            {
                stats?.SetWindowVisible(mainWindow.IsVisible);
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
            stats?.SetWindowVisible(false);
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

    // The Day plan tab's Sleep parts woken by the alarm end at bedtime plus the current sleep goal (ADR 33).
    private static DayPlanViewModel WithSleepGoal(DayPlanViewModel dayPlan, SleepSettingsService sleep)
    {
        dayPlan.SleepGoal = () => sleep.Current.Goal;
        // ADR 34: the phone's smart bedtime under each day type's Sleep part (workday types get the workday level).
        dayPlan.SmartNoteFor = workday => sleep.Current.UsePhoneBedtime
            && (workday ? sleep.PhoneBedtime?.Workdays : sleep.PhoneBedtime?.OffDays) is { } level
            ? AccountabiliBuddy.App.Services.Sync.PhoneBedtime.Describe(level) : null;
        return dayPlan;
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
            Title = AppBrand.Name,
            Width = 560,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(24) },
        };
        window.Closed += (_, _) => _desktop?.Shutdown();
        window.Show();
    }

    /// <summary>
    /// The spare key window (write down a new key), Emergency release (ADR 39) or Get a new spare key (ADR 41); one at a
    /// time. Quiet when nothing the user just did asked for it (a new key made by a timer or when a session ended): it
    /// shows without taking focus and its taskbar button flashes.
    /// </summary>
    private void ShowColdTurkeyKey(ColdTurkeyKeyMode mode, bool quiet = false)
    {
        if (_coldTurkeyKeys is null)
        {
            return;
        }

        if (_coldTurkeyKeyWindow is not null)
        {
            if (quiet)
            {
                FlashTaskbarButton(_coldTurkeyKeyWindow);
            }
            else
            {
                _coldTurkeyKeyWindow.Activate();
            }

            return;
        }

        var window = new ColdTurkeyKeyWindow { DataContext = new ColdTurkeyKeyViewModel(_coldTurkeyKeys, mode) };
        window.Closed += (_, _) => _coldTurkeyKeyWindow = null;
        _coldTurkeyKeyWindow = window;
        if (quiet)
        {
            window.ShowActivated = false;
            window.Show();
            FlashTaskbarButton(window);
            return;
        }

        window.Show();
        window.Activate();
    }

    private void FlashTaskbarButton(Window window)
    {
        if (window.TryGetPlatformHandle() is IPlatformHandle handle)
        {
            _windowAttention?.Flash(handle.Handle);
        }
    }

    private void CreateTrayIcon()
    {
        var quickItem = new NativeMenuItem($"Quick session…  ({_quickSession!.Preferences.Hotkey})");
        quickItem.Click += (_, _) => _quickSession.Show();

        var openItem = new NativeMenuItem($"Open {AppBrand.Name}");
        AppBrand.Changed += (_, _) => openItem.Header = $"Open {AppBrand.Name}";
        openItem.Click += (_, _) => ShowMainWindow();

        // Allowed during focus (Settings isn't): only redraws what would show anyway.
        var onScreenItem = new NativeMenuItem("Show on-screen window again");
        onScreenItem.Click += (_, _) => _sessionOverlay?.ShowAgain();

        // Allowed during focus: it pauses the countdown, it never shortens a wait.
        _stepAwayMenuItem = new NativeMenuItem("Step away");
        _stepAwayMenuItem.Click += (_, _) => _stepAway?.Toggle();

        _endSessionMenuItem = new NativeMenuItem("End session…");
        _endSessionMenuItem.Click += async (_, _) => await _endFlow!.TryEndSessionAsync(VisibleMainWindow());

        // ADR 39: the one way out of AB's Cold Turkey locks, with the spare key.
        var emergencyItem = new NativeMenuItem("Emergency release (Cold Turkey)…");
        emergencyItem.Click += (_, _) => ShowColdTurkeyKey(ColdTurkeyKeyMode.Emergency);

        // Allowed during focus, but it opens nothing then: it says so.
        var shutdownItem = new NativeMenuItem("Start shutdown");
        shutdownItem.Click += (_, _) => StartShutdownFromUi();

        var quitItem = new NativeMenuItem("Quit");
        quitItem.Click += async (_, _) => await QuitAsync();

        var menu = new NativeMenu();
        menu.Items.Add(quickItem);
        menu.Items.Add(openItem);
        menu.Items.Add(onScreenItem);
        menu.Items.Add(_stepAwayMenuItem);
        menu.Items.Add(_endSessionMenuItem);
        menu.Items.Add(shutdownItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(emergencyItem);
        menu.Items.Add(quitItem);

        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(IconUri)),
            ToolTipText = AppBrand.Name,
            Menu = menu,
        };
        _trayIcon.Clicked += (_, _) => ShowMainWindow();

        TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
        UpdateTray();
    }

    /// <summary>
    /// Shows the "Pick 3 for this session" window when a real session holds more tasks than it may (carried-over extras).
    /// It can't be skipped; it closes when answered or when the session ends.
    /// </summary>
    private void ShowPickTasks(SessionTasksService tasks, IMonitorLocator monitors)
    {
        if (_pickTasksWindow is not null || !tasks.NeedsPick)
        {
            return;
        }

        var card = new PickTasksViewModel(tasks);
        var window = new PickTasksWindow { DataContext = card };
        EventHandler<SessionState> onState = (_, state) =>
        {
            if (!state.IsRealSession)
            {
                Dispatcher.UIThread.Post(card.Dismiss); // The session ended first: nothing is parked.
            }
        };
        _sessions!.StateChanged += onState;
        card.Finished += (_, _) =>
        {
            _sessions.StateChanged -= onState;
            _pickTasksWindow = null;
            window.Close();
            ShowReadyCard(monitors); // Planning's "Ready?" card waited for this.
            ShowLunchRunIntoCard(monitors); // So did the run-into-lunch card.
        };
        if (monitors.Find(monitors.FindMonitorUnderCursor()) is MonitorArea area)
        {
            MonitorPlacement.Center(window, area);
        }

        _pickTasksWindow = window;
        ShowReadyCard(monitors); // Pick comes first: an already-open "Ready?" card closes until it is answered.
        ShowLunchRunIntoCard(monitors); // And the run-into-lunch card.
        _frontCards.Show(window);
    }

    /// <summary>
    /// Hides the front cards while the computer sleeps and, on waking, lets each service re-check against the current time and
    /// plan before the cards still due are shown again: a card for a state that ended during the sleep is never seen.
    /// </summary>
    private void WatchPowerTransitions(IMonitorLocator monitors)
    {
        _power = new PowerTransitions(_sessions!);
        _power.Suspending += (_, _) =>
        {
            _frontCards.Suspend();
            _presenter?.CloseReminders(); // 20 s reminder toasts would be stale on waking.
        };
        _power.Resumed += (_, _) =>
        {
            _taskReminders?.Refresh(); // reminders that came due while asleep ring now
            _ = _networkTime?.CheckAsync();
            // The cards stay hidden and registered while the services re-check: one that closes its card removes it, one that
            // opens a card only registers it. A full tick handles the session's sleep gap first, then every service checks again.
            int suspendCount = _frontCards.SuspendCount;
            try
            {
                _sessions!.TickNow();
            }
            finally
            {
                // Closes that the services posted (Pick's Dismiss, say) run before this: only then is what is left shown. If the
                // computer goes to sleep again before it runs, it is stale and does nothing.
                Dispatcher.UIThread.Post(() => _frontCards.Resume(monitors, suspendCount), DispatcherPriority.Background);
            }
        };
    }

    /// <summary>Shows or closes the Pre-Work window (ADR 31) as the service opens or closes it.</summary>
    private void ShowPreWork(IMonitorLocator monitors)
    {
        PreWorkPromptViewModel? prompt = _preWork?.Prompt;
        if (prompt is null)
        {
            PreWorkWindow? open = _preWorkWindow;
            _preWorkWindow = null;
            open?.Close();
            return;
        }

        if (_preWorkWindow is not null)
        {
            return;
        }

        prompt.AddComeback = text => _comebacks!.AddAsync(text);
        var window = new PreWorkWindow { DataContext = prompt };
        window.Closed += (_, _) =>
        {
            if (_preWorkWindow == window)
            {
                // Closed some other way: it opens again at the next check while still unanswered.
                _preWorkWindow = null;
                Dispatcher.UIThread.Post(() => ShowPreWork(monitors));
            }
        };
        if (monitors.Find(monitors.FindMonitorUnderCursor()) is MonitorArea area)
        {
            MonitorPlacement.Center(window, area);
        }

        _preWorkWindow = window;
        _frontCards.Show(window);
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
        _frontCards.Show(window);
    }

    /// <summary>Shows or closes the run-into-lunch card (a session that would overlap a fixed lunch) as the service opens or closes it.</summary>
    private void ShowLunchRunIntoCard(IMonitorLocator monitors)
    {
        // It waits behind the cards that come first (Pick, Ready?, the away card, the start-of-day assistant), like Ready? waits for
        // the away card, and shows when they close; the service keeps asking meanwhile. The away card's end-early dialog counts
        // too (the away card's own window is closed while it is open): the card shows after the dialog closes, which raises
        // the step-away service's CardChanged.
        bool otherCardUp = _pickTasksWindow is not null || _stepAway?.Card is not null || _stepAway is { IsEndDialogOpen: true }
            || _planFirst?.Card is not null || _startNudge?.AssistantCard is not null;
        LunchRunIntoCardViewModel? card = otherCardUp ? null : _lunchRunInto?.Card;
        if (card is null)
        {
            LunchRunIntoCardWindow? open = _lunchRunIntoWindow;
            _lunchRunIntoWindow = null;
            open?.Close();
            return;
        }

        if (_lunchRunIntoWindow is not null)
        {
            return;
        }

        var window = new LunchRunIntoCardWindow { DataContext = card };
        window.Closed += (_, _) =>
        {
            if (_lunchRunIntoWindow == window)
            {
                // Closed some other way: it is asked again at the 5-minute mark if still due.
                _lunchRunIntoWindow = null;
                _lunchRunInto?.CardWindowClosed();
            }
        };
        if (monitors.Find(monitors.FindMonitorUnderCursor()) is MonitorArea area)
        {
            MonitorPlacement.Center(window, area);
        }

        _lunchRunIntoWindow = window;
        _frontCards.Show(window); // Dimmed behind, kept in front, follows the virtual desktop.
    }

    /// <summary>Shows or closes the start nudge's card, like the work-hours gate, as the service opens or closes it.</summary>
    private void ShowStartNudgeCard(IMonitorLocator monitors)
    {
        StartNudgeCardViewModel? card = _startNudge?.Card;
        if (card is null)
        {
            StartNudgeCardWindow? open = _startNudgeCardWindow;
            _startNudgeCardWindow = null;
            open?.Close();
            return;
        }

        if (_startNudgeCardWindow is not null)
        {
            return;
        }

        var window = new StartNudgeCardWindow { DataContext = card };
        window.Closed += (_, _) =>
        {
            if (_startNudgeCardWindow == window)
            {
                // Closed some other way: it opens again at the next check.
                _startNudgeCardWindow = null;
                _startNudge?.CardWindowClosed();
            }
        };
        if (monitors.Find(monitors.FindMonitorUnderCursor()) is MonitorArea area)
        {
            MonitorPlacement.Center(window, area);
        }

        _startNudgeCardWindow = window;
        _frontCards.Show(window); // Dimmed behind, kept in front, follows the virtual desktop.
    }

    /// <summary>Shows or closes the start-of-day assistant's card, like the start nudge's, as the service opens or closes it.</summary>
    private void ShowStartOfDayCard(IMonitorLocator monitors)
    {
        StartOfDayCardViewModel? card = _startNudge?.AssistantCard;
        if (card is null)
        {
            StartOfDayWindow? open = _startOfDayWindow;
            _startOfDayWindow = null;
            open?.Close();
            return;
        }

        if (_startOfDayWindow is not null)
        {
            return;
        }

        var window = new StartOfDayWindow { DataContext = card };
        window.Closed += (_, _) =>
        {
            if (_startOfDayWindow == window)
            {
                // Closed some other way: it opens again at the next check.
                _startOfDayWindow = null;
                _startNudge?.AssistantWindowClosed();
            }
        };
        if (monitors.Find(monitors.FindMonitorUnderCursor()) is MonitorArea area)
        {
            MonitorPlacement.Center(window, area);
        }

        _startOfDayWindow = window;
        _frontCards.Show(window); // Dimmed behind, kept in front, follows the virtual desktop.
    }

    /// <summary>
    /// Shows, raises or closes the away card ("Back to the task?", a side task's length) as the step-away service opens it,
    /// asks again (unanswered cards come back every 2 minutes) or closes it. One window at a time, so it never stacks.
    /// </summary>
    private void ShowAwayCard(IMonitorLocator monitors)
    {
        // While the end-early dialog from the card is open the card's window is closed (never raised over the dialog).
        AwayCardViewModel? card = _stepAway is { IsEndDialogOpen: true } ? null : _stepAway?.Card;
        if (card is null)
        {
            AwayCardWindow? open = _awayCardWindow;
            _awayCardWindow = null;
            open?.Close();
            return;
        }

        if (_awayCardWindow is not null)
        {
            if (!ReferenceEquals(_awayCardWindow.DataContext, card))
            {
                _awayCardWindow.DataContext = card; // A different question in the same window.
            }

            // Raised again: back in front of everything.
            _frontCards.Raise(_awayCardWindow);
            return;
        }

        var window = new AwayCardWindow { DataContext = card };
        window.Closed += (_, _) =>
        {
            if (_awayCardWindow == window)
            {
                // Closed some other way: it opens again when the question is due (a side task's card at the next check).
                _awayCardWindow = null;
            }
        };
        if (monitors.Find(monitors.FindMonitorUnderCursor()) is MonitorArea area)
        {
            MonitorPlacement.Center(window, area);
        }

        _awayCardWindow = window;
        _frontCards.Show(window);
    }

    /// <summary>
    /// Shows, raises or closes the planning "Ready?" card as the plan-first service opens it, asks again (an unanswered one comes
    /// back every 2 minutes) or closes it. One window at a time, and none while the away card is up, so they never stack.
    /// </summary>
    private void ShowReadyCard(IMonitorLocator monitors)
    {
        // Not while the "Pick 3" prompt is up: it comes first.
        ReadyCardViewModel? card = _stepAway?.Card is not null || _pickTasksWindow is not null ? null : _planFirst?.Card;
        if (card is null)
        {
            ReadyCardWindow? open = _readyCardWindow;
            _readyCardWindow = null;
            open?.Close();
            return;
        }

        if (_readyCardWindow is not null)
        {
            if (!ReferenceEquals(_readyCardWindow.DataContext, card))
            {
                _readyCardWindow.DataContext = card;
            }

            // Raised again: back in front of everything.
            _frontCards.Raise(_readyCardWindow);
            return;
        }

        var window = new ReadyCardWindow { DataContext = card };
        window.Closed += (_, _) =>
        {
            if (_readyCardWindow == window)
            {
                // Closed some other way: it opens again at the next time it is due while planning is still unanswered.
                _readyCardWindow = null;
            }
        };
        if (monitors.Find(monitors.FindMonitorUnderCursor()) is MonitorArea area)
        {
            MonitorPlacement.Center(window, area);
        }

        _readyCardWindow = window;
        _frontCards.Show(window);
    }

    private void UpdateTray()
    {
        if (_trayIcon is null || _sessions is null || _configuration is null)
        {
            return;
        }

        SessionState state = _sessions.State;
        string? profileName = ProfileOptions.NameOf(_configuration.Current.Profiles, state.ProfileId);

        // Written only when the text changed: with no session it never does, and Windows is not asked to redraw a tooltip every second.
        string tip = state.IsActive
            ? $"{AppBrand.Name} · {SessionStatusFormatter.Title(state, profileName)} · {SessionStatusFormatter.Detail(state, _sessions.Now)}"
            : $"{AppBrand.Name} · no session running";
        if (_runningWaits?.Line() is string waitLine)
        {
            tip = $"{tip} · {waitLine}";
        }

        if (_trayIcon.ToolTipText != tip)
        {
            _trayIcon.ToolTipText = tip;
        }

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
        // ADR 68: ProductaBuddy watches no windows, private ones included.
        if (_privateWindowsNag is not null || _guidance?.IsCompanion == true)
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

    // Shows a dialog over the main window when it is open, else in the middle of the screen.
    private void ShowOwnedDialog(Window dialog)
    {
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

    // This PC is unlinked (ADR 54): the three choices, once per removal. With the window closed (a start at sign-in) a notice
    // with "Choose…" comes first. Closing the dialog without choosing leaves the PC unlinked; it comes back at the next start.
    private void ShowUnlinkedIfNew()
    {
        if (_syncRemoval?.Unlinked is not { } state || _unlinkedViewModel is null || _unlinkedShownFor == state.At)
        {
            return;
        }

        _unlinkedShownFor = state.At;
        if (_mainWindow is { IsVisible: true } || _presenter is null)
        {
            ShowUnlinkedDialog();
            return;
        }

        _presenter.ShowChoice(
            "This PC was removed from your synced devices",
            $"Removed by {state.RemovedByName}. Nothing syncs until you choose what to do.",
            "Choose…",
            () =>
            {
                ShowMainWindow();
                ShowUnlinkedDialog();
            },
            () => _unlinkedShownFor = null);
    }

    private void ShowUnlinkedDialog()
    {
        if (_unlinkedDialog is not null)
        {
            _unlinkedDialog.Activate();
            return;
        }

        if (_unlinkedViewModel is null || _syncRemoval?.Unlinked is null)
        {
            return;
        }

        _unlinkedDialog = new UnlinkedDialog { DataContext = _unlinkedViewModel };
        _unlinkedDialog.Closed += (_, _) => _unlinkedDialog = null;
        ShowOwnedDialog(_unlinkedDialog);
    }

    // The account takes over this PC's own default view and picked calendar once (only where it has none yet); then they are cleared
    // here. The take-over is in memory when the first await comes, so the export never runs without it. Logs what kind, never a value.
    private static async Task AdoptLegacyTaskSettingsAsync(
        AccountabiliBuddy.App.Services.Tasks.TaskAccountSettingsService account,
        AccountabiliBuddy.App.Services.Tasks.TaskSettingsService settings,
        AccountabiliBuddy.App.Services.Tasks.TaskReminderSettingsService reminders)
    {
        Guid? defaultView = settings.LegacyDefaultViewBlocking();
        string? picked = reminders.Current.PickedCalendarId;
        if (defaultView is null && picked is null)
        {
            return;
        }

        try
        {
            await account.AdoptLegacyAsync(defaultView, picked);
            if (defaultView is Guid adopted)
            {
                await settings.ClearLegacyDefaultViewAsync(adopted);
            }

            if (picked is not null)
            {
                await reminders.ClearLegacyPickedCalendarAsync();
            }

            System.Diagnostics.Trace.TraceInformation(
                "Tasks settings now the account's: default view " + (defaultView is null ? "none" : "taken over") + ", picked calendar " + (picked is null ? "none" : "taken over"));
        }
        catch (Exception ex)
        {
            // Kind only. The old values stay, so the next start tries again.
            System.Diagnostics.Trace.TraceError("Taking over the Tasks settings failed: " + ex.GetType().Name);
        }
    }

    // The built-in views exist and the default one is open; the old filter screen's saved filters follow as views from then on.
    private async Task StartTaskViewsAsync()
    {
        if (_taskViews is null || _filterPresets is null)
        {
            return;
        }

        await _taskViews.StartAsync();
        _taskViews.FollowPresets(_filterPresets);
    }

    // ADR 76: the user chose to rebuild an unreadable task list: set it aside (after a database copy), then start again so the
    // migration runs; during a focus session the app isn't restarted, the rebuild happens at the next start.
    private async Task RebuildTaskListsAsync(LocalDatabase database, SqliteJsonDocumentStore documents, TimeProvider time, string dataDirectory)
    {
        await AccountabiliBuddy.App.Services.Tasks.TaskModelMigration.SetAsideForRebuildAsync(database, documents, time, dataDirectory);
        if (_sessions?.State.IsActive == true || !TryRestartApp())
        {
            _presenter?.ShowNotice("Task lists rebuild at the next start", "They come back when the app starts again.");
        }
    }

    /// <summary>Starts a new copy (which waits for this one to exit) and quits, e.g. to load restored settings.</summary>
    private void RestartApp()
    {
        TryRestartApp();
    }

    /// <returns>False when the new copy could not be started (nothing was shut down).</returns>
    private bool TryRestartApp()
    {
        string? executable = Environment.ProcessPath;
        if (executable is null)
        {
            return false;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(executable, StartupOptions.RestartedArgument) { UseShellExecute = false });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }

        if (_mainWindow is not null)
        {
            _mainWindow.AllowClose = true;
        }

        _desktop?.Shutdown();
        return true;
    }

    /// <param name="quick">The first run's three questions; otherwise the full setup.</param>
    /// <summary>The migration assistant: the pages of features not set up yet, or a notice when there are none.</summary>
    private void ShowWhatsNew()
    {
        List<AssistantStep> steps = _setupFeatures?.Pending.Select(feature => feature.Step).Distinct().ToList() ?? new List<AssistantStep>();
        if (steps.Count == 0)
        {
            _presenter?.ShowNotice("Nothing new to set up", "Everything added since your last version is set up.");
            return;
        }

        ShowSetupAssistant(quick: false, whatsNew: steps);
    }

    private void ShowSetupAssistant(bool quick, IReadOnlyList<AssistantStep>? whatsNew = null)
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
        if (quick)
        {
            viewModel.BeginQuick();
        }
        else if (whatsNew is not null)
        {
            viewModel.BeginWhatsNew(whatsNew);
        }

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

    // "Work over for today" (or its end): Today's line and the capture box's wording follow.
    private void ShowWorkOver(SessionViewModel session)
    {
        ShutdownWorkOver? workOver = _shutdown?.WorkOver;
        session.WorkOver = workOver;
        if (session.Comebacks is not null)
        {
            session.Comebacks.IsWorkOver = workOver is not null;
        }
    }

    // The sync file's name for an armed end's kind (ADR 66).
    private static string SyncKindOf(ArmedEndKind kind) => kind switch
    {
        ArmedEndKind.EndSession => AccountabiliBuddy.Core.Sync.SyncArmedEnd.KindEndSession,
        ArmedEndKind.EndWorkEarly => AccountabiliBuddy.Core.Sync.SyncArmedEnd.KindEndWorkEarly,
        ArmedEndKind.StopForToday => AccountabiliBuddy.Core.Sync.SyncArmedEnd.KindStopForToday,
        ArmedEndKind.Companion => AccountabiliBuddy.Core.Sync.SyncArmedEnd.KindCompanion,
        _ => AccountabiliBuddy.Core.Sync.SyncArmedEnd.KindPreWorkDayOff,
    };

    private void StartShutdownFromUi() => _ = StartShutdownFromUiAsync();

    // "Start shutdown" from Today, the tray or the reminder: the routine opens on Today. During a session it doesn't open; it says so.
    // During a work part (ADR 65) it first asks to end work early, with the end-early wait and reason; cancelling changes nothing and
    // opens nothing. Outside a work part, or with a card already open, it opens as before.
    private async Task StartShutdownFromUiAsync()
    {
        if (_shutdown is null || _sessions is null)
        {
            return;
        }

        // Already confirmed and counting down (ADR 66): Today and the tile show it, with Cancel.
        if (_armedEnds?.Find(ArmedEndKind.EndWorkEarly) is not null)
        {
            ShowMainWindow();
            return;
        }

        if (!_sessions.State.IsActive && _shutdown.Current is null && _workEnded?.Propose() is WorkEndProposal proposal
            && !await ConfirmEndWorkEarlyAsync(proposal))
        {
            return;
        }

        if (!_shutdown.StartByHand())
        {
            _presenter?.ShowNotice("Shutdown waits", "The routine doesn't open during a session. Start it once the session ends.");
            return;
        }

        if (_mainWindow?.DataContext is MainWindowViewModel main)
        {
            main.SelectedTab = (int)MainTab.Today;
        }

        ShowMainWindow();
    }

    // "End work early?": the wait and reason of ending a session early (the same Bypass settings), and "Also end later work today" when
    // more work comes later the same day. True once work was ended (or no work part runs any more); false when cancelled.
    private async Task<bool> ConfirmEndWorkEarlyAsync(WorkEndProposal proposal)
    {
        if (_endWorkDialog is not null)
        {
            _endWorkDialog.Activate();
            return false;
        }

        using var viewModel = new EndWorkEarlyDialogViewModel(proposal, _configuration!.Current.Bypass, _sessions!.TimeProvider);
        var dialog = new EndWorkEarlyDialog { DataContext = viewModel };
        _endWorkDialog = dialog;
        bool confirmed;
        try
        {
            confirmed = _mainWindow is { IsVisible: true } owner
                ? await dialog.ShowDialog<bool>(owner)
                : await ShowWithoutOwnerAsync(dialog);
        }
        finally
        {
            _endWorkDialog = null;
        }

        // Never during a session: one started while the dialog was up keeps the work block as it is.
        if (!confirmed || _sessions.State.IsActive)
        {
            return false;
        }

        // Confirmed during the wait (ADR 66): work ends by itself when the wait is over, then the routine opens; nothing opens now.
        if (viewModel.Armed && _armedEnds!.Arm(
                ArmedEndKind.EndWorkEarly, viewModel.RequestedAt, viewModel.Wait, viewModel.Reason,
                workEnd: new ArmedWorkEnd(proposal.PlanDate, proposal.Current, proposal.Later, viewModel.AlsoEndLater)) is not null)
        {
            return false;
        }

        // The block the dialog showed is the one ended; if it is over by now, nothing is ended.
        if (!await _workEnded!.EndAsync(proposal, viewModel.AlsoEndLater, viewModel.Reason))
        {
            _presenter?.ShowNotice("Nothing ended", "That work block was already over, so nothing was ended.");
        }

        return true;
    }

    private static Task<bool> ShowWithoutOwnerAsync(EndWorkEarlyDialog dialog)
    {
        var completion = new TaskCompletionSource<bool>();
        dialog.Closed += (_, _) => completion.TrySetResult(dialog.Confirmed);
        dialog.Show();
        dialog.Activate();
        return completion.Task;
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

        if (_mainWindow.DataContext is MainWindowViewModel { SelectedTab: (int)MainTab.Today or (int)MainTab.Goals })
        {
            _beeminder?.RefreshIfStale();
        }

        if (_calendarSync is not null && DateTimeOffset.UtcNow - _lastWindowRefresh >= WindowRefreshInterval)
        {
            _lastWindowRefresh = DateTimeOffset.UtcNow;
            _ = _calendarSync.RefreshAllAsync();
        }
    }

    /// <summary>
    /// Today without taking focus: a hidden window is shown minimized, a visible one stays where it is, and the
    /// taskbar button flashes until the user brings it forward. Never Activate() or RequestFocus().
    /// </summary>
    private void ShowMainWindowQuietly()
    {
        if (_mainWindow is null)
        {
            return;
        }

        if (_mainWindow.DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SelectedTab = (int)MainTab.Today;
        }

        if (!_mainWindow.IsVisible)
        {
            // Minimized before Show(), and not activated, so nothing appears over what the user is typing in.
            bool showActivated = _mainWindow.ShowActivated;
            _mainWindow.WindowState = WindowState.Minimized;
            _mainWindow.ShowActivated = false;
            _mainWindow.Show();
            _mainWindow.ShowActivated = showActivated;
        }

        if (_mainWindow.TryGetPlatformHandle() is IPlatformHandle handle)
        {
            _windowAttention?.Flash(handle.Handle);
        }
    }

    // ADR 50: where the day part a task was planned for starts now (the plan may have been edited since); the time it was planned
    // with when the part is gone.
    private DateTime PlannedPartStart(PlannedSlot slot)
    {
        var day = new LocalWindow(slot.Date.AddDays(-1).ToDateTime(TimeOnly.MinValue), slot.Date.AddDays(2).ToDateTime(TimeOnly.MinValue));
        PlannedPart? part = _configuration!.Current.DayPlan.PartsIn(day)
            .FirstOrDefault(planned => planned.Part.Id == slot.PartId && DateOnly.FromDateTime(planned.Window.Start) == slot.Date);
        return part?.Window.Start ?? slot.StartsAt;
    }

    // Q375: the older daily-goal screens set the goal through GoalsService's one rule and get back what to tell the user (null: applied).
    private async Task<string?> SetDailyGoalAsync(GoalService daily, int minutes)
    {
        if (_goals is not { } goals)
        {
            // Only before the goals service exists (never once the window is up): the stricter half of the rule, raising only.
            if (minutes > daily.Current.DailyMinutes)
            {
                await daily.SetAsync(minutes);
                return null;
            }

            return minutes == daily.Current.DailyMinutes ? null : "Lowering the goal isn't possible right now. Raising it is fine.";
        }

        return goals.DailyGoalNote(await goals.SetDailyGoalAsync(minutes));
    }

    // New goal (goalId null) or Edit goal at a section, over the main window (round 2 R5.4), or over the weekly review (`owner`) with the
    // review's suggested numbers pre-filled (`suggested`). Labels, day plan categories and Beeminder's cached goals are read as it opens;
    // nothing is fetched.
    private async Task ShowGoalEditorAsync(
        AccountabiliBuddy.App.Services.Goals.GoalSourceDataService data, Guid? goalId, GoalEditSection section, bool focusActive,
        SessionTasksService sessionTasks, GuidanceModeService guidance, AccountabiliBuddy.Core.Goals.GoalReviewAdjustment? suggested = null,
        Visual? owner = null)
    {
        if (_goals is not { } goals || !goals.Store.IsReady)
        {
            return;
        }

        AccountabiliBuddy.Core.Goals.Goal? goal = goalId is Guid id ? goals.Lists.Goals.FirstOrDefault(item => item.Id == id && item.IsLive) : null;
        if (goalId is not null && goal is null)
        {
            return;
        }

        var labels = sessionTasks.Categories
            .Select(category => new GoalChoice(AccountabiliBuddy.Core.Domain.TaskLabel.IdFor(category.Name).ToString(), category.Name,
                Brush: GoalIcons.Brush(category.Color)))
            .ToList();
        var categories = _configuration!.Current.DayPlan.Categories
            .Select(category => new GoalChoice(category.Id.ToString(), category.Name, Brush: GoalIcons.Brush(category.Color)))
            .ToList();
        var beeminder = (_beeminder?.Goals ?? [])
            .Select(item => new GoalChoice(item.Key, item.Title, Note: $"{(item.Needed.Length > 0 ? item.Needed + " · " : string.Empty)}{item.SafeDays} safe days"))
            .ToList();
        string? beeminderRead = _beeminderDatapoints is { LastReadAt: DateTimeOffset readAt }
            ? $"Read {TimeZoneInfo.ConvertTime(readAt, data.Zone).ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture)}"
            : null;
        string? beeminderProblem = _beeminderDatapoints is { Failed: true } ? "Couldn't reach Beeminder · keeps the last values" : null;
        var lookups = new GoalEditorLookups(labels, categories, _beeminder?.HasToken == true, beeminder, guidance.IsCompanion, beeminderRead, beeminderProblem);
        var editor = new GoalEditorViewModel(goals, data, lookups, goal, section, data.Today, data.Zone, focusActive);
        if (suggested is not null)
        {
            editor.Target.ApplySuggestion(suggested);
        }

        Guid? created = await GoalEditorDialog.ShowAsync(owner ?? VisibleMainWindow(), editor, goals);
        if (created is Guid made && GoalsPageOfWindow() is { } page)
        {
            page.Select(made);
        }
    }

    // The weekly goals review (round 2 R5.4) over the main window; never during a session (it closes if one starts, keeping its answers).
    // "Adjust the target" opens Edit goal › Target over it with the suggested numbers. With Settings › Goals' "Run the tidy-up straight
    // after" on, the tidy-up follows the last answer; with every goal already answered, Start goes straight to that tidy-up.
    private async Task ShowGoalReviewAsync(
        AccountabiliBuddy.App.Services.Goals.GoalReviewService review, AccountabiliBuddy.App.Services.Goals.GoalSourceDataService data,
        SessionTasksService sessionTasks, GuidanceModeService guidance, AccountabiliBuddy.App.Services.Tasks.TaskStore taskStore, TimeProvider time)
    {
        if (review.SessionRunning || !review.Goals.Store.IsReady)
        {
            return;
        }

        DateOnly week = review.WeekOf(review.DueAt(review.Now));
        bool thenTidyUp;
        if (!AccountabiliBuddy.App.Services.Goals.GoalReviewService.IsComplete(review.RecordFor(week), review.GoalsToReview(data.Today)))
        {
            var viewModel = new GoalReviewViewModel(review);
            var dialog = new GoalReviewDialog(viewModel);
            viewModel.AdjustTarget = (goalId, suggested) => ShowGoalEditorAsync(data, goalId, GoalEditSection.Target, false, sessionTasks, guidance, suggested, dialog);
            await dialog.ShowOverAsync(VisibleMainWindow());
            thenTidyUp = viewModel.ThenTidyUp;
        }
        else
        {
            thenTidyUp = review.TidyUpDue;
        }

        if (thenTidyUp && !review.SessionRunning && _taskTidyUp is { } tidyUp && tidyUp.Start() is not null)
        {
            await TidyUpDialog.ShowAsync(VisibleMainWindow(), new TidyUpViewModel(tidyUp, taskStore, time));
        }

        review.NotifyChanged();
    }

    private GoalsViewModel? GoalsPageOfWindow() => (_mainWindow?.DataContext as MainWindowViewModel)?.Goals;

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

    // ADR 60: says what a gap did to the session. A session closed by it is always told; the rest only with the setting on.
    private void ShowSessionGap(SessionGapReport report, EnforcementPresenter presenter)
    {
        string off = DescribeGap(report.Gap);
        string cause = SessionGapCause(report);
        string wentOff = TimeZoneInfo.ConvertTime(report.LastAliveAt, _sessions!.TimeProvider.LocalTimeZone).ToString("t");
        switch (report.Outcome)
        {
            case Core.Domain.SessionGapOutcome.ClosedLongGap:
                presenter.ShowNotice("Session closed", $"Your PC was off for {off}{cause}, so the session ended when it went off, at {wentOff}. Only the time before that counts.");
                return;
            case Core.Domain.SessionGapOutcome.EndPassed:
                string planned = report.OriginalEnd is DateTimeOffset end ? TimeZoneInfo.ConvertTime(end, _sessions.TimeProvider.LocalTimeZone).ToString("t") : "Its end";
                presenter.ShowNotice("Session closed", $"Your PC was off for {off}{cause}. Its planned end, {planned}, passed meanwhile, so it ended when the PC went off, at {wentOff}. Only the time before that counts.");
                return;
            case Core.Domain.SessionGapOutcome.Quiet:
                return;
        }

        if (!_pcOff!.Current.TellMe)
        {
            return;
        }

        string timer = report.Outcome == Core.Domain.SessionGapOutcome.KeptEnd
            ? "Your session kept its original end time."
            : _sessions.State switch
            {
                { IsAway: true } => "Your session is still stepped away.",
                { IsPlanning: true } => "Your session is still planning.",
                { PhaseEndsAt: DateTimeOffset phaseEnd } => $"Timer paused: {Math.Max(1, (int)Math.Ceiling((phaseEnd - _sessions.Now).TotalMinutes))} min left.",
                _ => "Timer paused. That time isn't counted.",
            };
        presenter.ShowChoice("Your PC was off", $"It was off for {off}{cause}. {timer}", "End session…", () => _ = _endFlow!.TryEndSessionAsync(VisibleMainWindow()), () => { });
    }

    // What the PC can tell about why it was off, to word the notice only.
    private string SessionGapCause(SessionGapReport report)
    {
        if (report.Saved is not SavedSession saved)
        {
            return " (it was asleep)";
        }

        if (saved.BootId is not null && saved.BootId == new RegistryBootIdProvider().GetBootId())
        {
            return $" ({AppBrand.Name} was closed)";
        }

        Core.Domain.ShutdownCause cause = new AccountabiliBuddy.Platform.Windows.Power.EventLogShutdownCauseReader().Read(saved.LastAliveAt - TimeSpan.FromMinutes(1));
        return cause switch
        {
            Core.Domain.ShutdownCause.WindowsUpdate => " (looks like Windows Update restarted it)",
            Core.Domain.ShutdownCause.Unexpected => " (looks like it lost power or crashed)",
            Core.Domain.ShutdownCause.ShutDown => " (it was shut down or restarted)",
            _ => saved.WindowsEnding ? " (it was shut down or restarted)" : string.Empty,
        };
    }

    // ADR 60: a file at least every 5 minutes while a session runs, so other devices can tell a quiet PC from a slow sync.
    private void WriteSessionHeartbeat()
    {
        if (_sessions is not { State.IsRealSession: true } || _sessions.Now - _lastSessionWrite < SessionHeartbeat)
        {
            return;
        }

        _lastSessionWrite = _sessions.Now;
        _ = WriteSyncFilesAsync();
    }

    private Task WriteSyncFilesAsync()
    {
        return Task.WhenAll(
            _syncFolder?.WriteNowAsync() ?? Task.CompletedTask,
            _syncDrive?.WriteNowAsync() ?? Task.CompletedTask,
            _syncRelay?.WriteNowAsync() ?? Task.CompletedTask);
    }

    // ADR 60: a shutdown that was cancelled leaves GoingOffAt set; the app is still running well after the 2-second write, so Windows
    // did not go through with it. Cleared so later files stop saying "off" (the phone would drop its guards after 10 minutes).
    private void ClearStaleGoingOff()
    {
        if (_syncCoordinator?.GoingOffAt is DateTimeOffset goingOffAt && _sessions is not null && _sessions.Now - goingOffAt > GoingOffStale)
        {
            _syncCoordinator.GoingOffAt = null;
            _windowsEndingHandled = false;
        }
    }

    // ADR 60: Windows is shutting down with a session running. Noted for the next boot, and one last file tells the other devices;
    // only about 2 seconds are spent on it, and the quiet check covers a file that doesn't make it.
    private void OnWindowsEnding()
    {
        if (_windowsEndingHandled || _sessions is not { State.IsRealSession: true })
        {
            return;
        }

        _windowsEndingHandled = true;
        _checkpoints?.MarkWindowsEnding();
        if (_syncCoordinator is null)
        {
            return;
        }

        _syncCoordinator.GoingOffAt = _sessions.Now;
        using var budget = new CancellationTokenSource(GoingOffWriteBudget);
        Task writes = WriteSyncFilesAsync();
        _ = writes.ContinueWith(_ => { try { budget.Cancel(); } catch (ObjectDisposedException) { } }, TaskScheduler.Default);
        Dispatcher.UIThread.MainLoop(budget.Token);
    }

    private static string DescribeGap(TimeSpan gap)
    {
        return gap.TotalMinutes < 1 ? "less than a minute"
            : gap.TotalHours < 1 ? $"{(int)gap.TotalMinutes} min"
            : $"{(int)gap.TotalHours} h {gap.Minutes} min";
    }

    private async Task QuitAsync()
    {
        // ADR 59: while the safeguard holds, Quit is refused and says why.
        if (_quitGuard?.CurrentLock() is QuitLock quitLock)
        {
            _presenter?.ShowNotice("Quit is locked", QuitGuardService.Describe(quitLock));
            return;
        }

        // A task ticked a moment ago is still in its strike-and-fold: store it now (before the session ends, so a session that ends at
        // once archives its checklist with that tick in it), so the tick isn't lost.
        if (_mainWindow?.DataContext is MainWindowViewModel { Tasks: { } tasksPage })
        {
            await tasksPage.FlushPendingFinishesAsync();
        }

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

        _goalCard?.Dispose();
        _weekCard?.Dispose();
        _headphones?.Dispose();

        if (_mainWindow is not null)
        {
            _mainWindow.AllowClose = true;
        }

        _desktop?.Shutdown();
    }

    private void DisposeServices()
    {
        // ADR 59: every clean exit passes here, so nothing brings the app back after one.
        _quitGuard?.StandDown();
        _instanceSignals?.Dispose();
        _hotkey?.Dispose();
        _stepAway?.Dispose();
        _planFirst?.Dispose();
        _sessionReview?.Dispose();
        _workHours?.Dispose();
        _capture?.Dispose();
        _power?.Dispose();
        _taskReminders?.Dispose();
        _taskReminderRinger?.Dispose();
        _goalReview?.Dispose();
        _taskCalendarExport?.Dispose();
        _workGate?.Dispose();
        _hyperfocus?.Dispose();
        _deepWork?.Dispose();
        _shutdown?.Dispose();
        _stepAwayHotkey?.Dispose();
        _enforcement?.Dispose();
        _recorder?.Dispose();
        _watchedBrowsers?.Dispose();
        _browserSource?.Dispose();
        _activityWatcher?.Dispose();
        _checkpoints?.Dispose();
        _sessionOverlay?.Dispose();
        _overlayStickyHotkey?.Dispose();
        _overlayHideHotkey?.Dispose();
        _panelKeys?.Dispose();
        _ctrlAltWatcher?.Dispose();
        _batteryReader?.Dispose();
        _taskbarGeometry?.Dispose();
        _noise?.Dispose();
        _soundPlayer?.Dispose();
        _updates?.Dispose();
        _appearance?.Dispose();
        _dayBar?.Dispose();
        _privateAccess?.Dispose();
        _coldTurkey?.Dispose();
        _coldTurkeyKeys?.Dispose();
        _syncFolder?.Dispose();
        _syncDrive?.Dispose();
        _syncRelay?.Dispose();
        _relayHost?.Dispose();
        _qrSync?.Dispose();
        _clockAnchors?.Dispose();
        _networkTime?.Dispose();
        _autoBackup?.Dispose();
        _unlocks?.Dispose();
        _armedEnds?.Dispose();
        _calendarSync?.Dispose();
        _beeminder?.Dispose();
        _calendarNetwork?.Dispose();
        _sessions?.Dispose();
        _trayIcon?.Dispose();
    }
}
