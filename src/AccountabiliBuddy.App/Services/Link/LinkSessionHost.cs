using System.Text.Json;
using AccountabiliBuddy.App.Services.Backup;
using AccountabiliBuddy.App.Services.Remote;
using AccountabiliBuddy.App.Services.Sync;
using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Link;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Link;

/// <summary>
/// The app's side of the link (<see cref="ILinkSessionHost"/>): the running session, the profiles and the count-up
/// profile, starting a session, ending one early by the same rules as the End session dialog, and the settings the
/// device shows (profiles, sleep, day bar, upcoming events; L2), today's focus (ADR 21) and the sync files (ADR 22,
/// once <see cref="UseSync"/> is called). Every call runs on the UI thread, where the session lives.
/// </summary>
public sealed class LinkSessionHost : ILinkSessionHost
{
    // The documents a backup also carries for the phone (see BackupService.SettingsKeys).
    private static readonly string[] SettingsKeys = { "sleep", "daybar" };

    // Events from yesterday to this many days ahead, as the PC's day bar shows them (visible calendars).

    // While a session runs its time and drift grow, so today's focus is sent again this often (and at midnight).
    private static readonly TimeSpan FocusDayRefresh = TimeSpan.FromMinutes(5);

    private readonly SessionController _sessions;
    private readonly ConfigurationService _configuration;
    private readonly QuickSessionService _quickSession;
    private readonly SqliteJsonDocumentStore _documents;
    private readonly CalendarService _calendars;
    private readonly RemoteSessionControl _control;
    private readonly RemotePlanEdits _planEdits;
    private readonly IActivityLog _log;
    private readonly OmittedSessionsService _omitted;
    private readonly DeletedUsageService _deleted;
    private readonly PrivacySettingsService _privacy;
    private readonly DispatcherTimer _focusDayTimer;
    private DateOnly _focusDayDate;

    // Device sync (ADR 22), set by UseSync.
    private SyncCoordinator? _sync;
    private ReceivedDevicesService? _received;
    private SyncIdentityService? _identity;
    private SyncSettingsService? _syncSettings;

    // This PC's own sealed file, kept until its data changes: the link sends a file again only when its bytes change.
    private byte[]? _ownSyncFile;
    private int _ownSyncFileGeneration;

    /// <summary>Distracted stretches marked in History, folded into the records; read again when they change. Set by the app.</summary>
    public SessionGapsService? Gaps
    {
        get => _gaps;
        set
        {
            _gaps = value;
            if (value is not null)
            {
                value.Changed += (_, _) => FocusDayChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private SessionGapsService? _gaps;

    private IReadOnlyList<SessionRecord> WithGaps(IReadOnlyList<SessionRecord> records) => _gaps?.Apply(records) ?? records;

    public LinkSessionHost(
        SessionController sessions,
        ConfigurationService configuration,
        QuickSessionService quickSession,
        SqliteJsonDocumentStore documents,
        CalendarService calendars,
        SleepSettingsService sleep,
        DayBarSettingsService dayBar,
        IActivityLog log,
        OmittedSessionsService omitted,
        DeletedUsageService deleted,
        PrivacySettingsService privacy,
        RemoteSessionControl control,
        RemotePlanEdits planEdits)
    {
        _sessions = sessions;
        _configuration = configuration;
        _quickSession = quickSession;
        _documents = documents;
        _calendars = calendars;
        // Shared with the sync files (ADR 42): starting, ending early and plan edits follow the same rules either way.
        _control = control;
        _planEdits = planEdits;

        _sessions.StateChanged += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
        _configuration.Changed += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
        _quickSession.CountUpProfileChanged += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);

        // The link sends them only when they really changed (it compares a hash), so raising often is fine.
        _configuration.Changed += (_, _) => SettingsChanged?.Invoke(this, EventArgs.Empty);
        sleep.Changed += (_, _) => SettingsChanged?.Invoke(this, EventArgs.Empty);
        dayBar.Changed += (_, _) => SettingsChanged?.Invoke(this, EventArgs.Empty);
        calendars.Changed += (_, _) => SettingsChanged?.Invoke(this, EventArgs.Empty);

        _log = log;
        _omitted = omitted;
        _deleted = deleted;
        _privacy = privacy;
        _focusDayDate = Today();
        _sessions.StateChanged += (_, _) => FocusDayChanged?.Invoke(this, EventArgs.Empty);
        _configuration.Changed += (_, _) => FocusDayChanged?.Invoke(this, EventArgs.Empty);
        omitted.Changed += (_, _) => FocusDayChanged?.Invoke(this, EventArgs.Empty);
        deleted.Changed += (_, _) => FocusDayChanged?.Invoke(this, EventArgs.Empty);
        // ADR 15: turning Activity history off stops sending it (the plan still goes).
        privacy.Changed += (_, _) => FocusDayChanged?.Invoke(this, EventArgs.Empty);
        _focusDayTimer = new DispatcherTimer { Interval = FocusDayRefresh };
        _focusDayTimer.Tick += (_, _) => OnFocusDayTick();
        _focusDayTimer.Start();
    }

    public string PcName => Environment.MachineName;

    public DateTimeOffset Now => _sessions.Now;

    public event EventHandler? StateChanged;

    public event EventHandler? SettingsChanged;

    public event EventHandler? FocusDayChanged;

    /// <summary>The manual refresh: sends the paired devices the settings and today's focus again.</summary>
    public void PushAll()
    {
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        FocusDayChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? SyncFilesChanged;

    /// <summary>Raised (on the UI thread) with the reason when a linked device ended the session early.</summary>
    public event EventHandler<string>? EndedFromDevice;

    public Task<LinkState> GetStateAsync()
    {
        return Dispatcher.UIThread.InvokeAsync(BuildState).GetTask();
    }

    public Task<JsonElement> GetSettingsAsync()
    {
        return Dispatcher.UIThread.InvokeAsync(BuildSettingsAsync);
    }

    /// <summary>A backup's shape, so the phone reads it as it reads backups; nothing in it changes unless the data did.</summary>
    private async Task<JsonElement> BuildSettingsAsync()
    {
        string profiles = AccountabiliBuddy.Core.Storage.ProfileDocumentSerializer.Serialize(_configuration.Current);
        var settings = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (string key in SettingsKeys)
        {
            if (await _documents.LoadAsync<JsonElement?>(key) is JsonElement value)
            {
                settings[key] = value;
            }
        }

        DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_sessions.Now, _sessions.TimeProvider.LocalTimeZone).DateTime);
        // Each event with its calendar's colour, for the phone's Calendar row (an older phone ignores "color").
        List<System.Text.Json.Nodes.JsonObject> events = DeviceCalendarEvents.Build(_calendars, today);

        var content = new
        {
            profiles,
            settings,
            calendarEvents = events,
            appVersion = BackupService.AppVersion,
            // ADR 19: what a phone's day plan edit is based on, and when the PC's plan last changed.
            dayPlanVersion = DayPlanVersion.Of(_configuration.Current.DayPlan),
            dayPlanEditedAt = _planEdits.PlanEditedAt,
        };
        return JsonSerializer.SerializeToElement(content, LinkFraming.JsonOptions);
    }

    /// <summary>The history changed: a session added, edited or deleted by hand (ADR 20), new activity, or "Delete my data".</summary>
    public void NotifyHistoryChanged()
    {
        FocusDayChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task<LinkFocusDay> GetFocusDayAsync()
    {
        return Dispatcher.UIThread.InvokeAsync(BuildFocusDayAsync);
    }

    /// <summary>
    /// Today's focus from the history, as the day bar counts it; the plan alone while Activity history is off (ADR 15).
    /// When the history can't be read this throws, and the link sends nothing (devices keep what they had).
    /// </summary>
    private async Task<LinkFocusDay> BuildFocusDayAsync()
    {
        TimeZoneInfo zone = _sessions.TimeProvider.LocalTimeZone;
        DateTimeOffset now = _sessions.Now;
        DateOnly today = Today();
        DateTime todayStart = today.ToDateTime(TimeOnly.MinValue);
        IReadOnlyList<SessionRecord> records = Array.Empty<SessionRecord>();
        IReadOnlyList<DriftStretch> drift = Array.Empty<DriftStretch>();
        if (_privacy.Current.ActivityLogEnabled)
        {
            // The week and the day before it, so a session that ran into the first day has its start.
            records = WithGaps(SessionHistoryBuilder.Build(await _log.GetSessionEventsAsync(At(todayStart.AddDays(-7), zone), At(todayStart.AddDays(2), zone))));
            List<TimeRange> leftOut = records
                .Where(record => _omitted.IsOmitted(record.StartedAt) || record.Mode == SessionMode.Scheduled)
                .Select(record => new TimeRange(record.StartedAt, record.EndedAt ?? now))
                .ToList();
            drift = await _log.GetDriftAsync(At(todayStart, zone), At(todayStart.AddDays(1), zone), leftOut, _deleted.Parts);
        }

        IReadOnlyList<FocusProfile> profiles = _configuration.Current.Profiles;
        return FocusDayBuilder.Build(today, records, drift, _configuration.Current.DayPlan, _omitted.IsOmitted,
            id => ProfileOptions.NameOf(profiles, id), _sessions.State.IsRealSession, now, zone) with { GoalMinutes = GoalMinutes?.Invoke() };
    }

    /// <summary>Every few minutes while a session runs, and when the date changes.</summary>
    private void OnFocusDayTick()
    {
        DateOnly today = Today();
        if (_sessions.State.IsRealSession || today != _focusDayDate)
        {
            _focusDayDate = today;
            FocusDayChanged?.Invoke(this, EventArgs.Empty);
            // The running session's file grows with it too.
            OnSyncDataChanged();
        }
    }

    /// <summary>
    /// Lets the link carry sync files (ADR 22): this PC's own and the last one received from each other device, and
    /// files a phone carried. The sync services are built after the link, so they are passed here, once.
    /// </summary>
    public void UseSync(SyncCoordinator coordinator, ReceivedDevicesService received, SyncIdentityService identity, SyncSettingsService settings)
    {
        _sync = coordinator;
        _received = received;
        _identity = identity;
        _syncSettings = settings;
        // This PC's own data (also "Delete my data"), a new group (another key) or the switch: its own file is sealed again.
        coordinator.OwnDataChanged += (_, _) => OnSyncDataChanged();
        identity.Changed += (_, _) => OnSyncDataChanged();
        settings.Changed += (_, _) => OnSyncDataChanged();
        // A newer file to pass on. Its own file isn't sealed again just for it (OwnDataChanged comes only when the file
        // changed what this PC's own says), so a phone and this PC never answer each other's "seen" times forever.
        received.Changed += (_, _) => SyncFilesChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task<IReadOnlyList<byte[]>> GetSyncFilesAsync()
    {
        return Dispatcher.UIThread.InvokeAsync(BuildSyncFilesAsync);
    }

    /// <summary>
    /// This PC's own file (sealed again only after its data changed) and each received one as it arrived: a merged copy
    /// is never re-sealed and passed on. Without its own file while the history can't be read.
    /// </summary>
    private async Task<IReadOnlyList<byte[]>> BuildSyncFilesAsync()
    {
        if (_sync is null || _received is null || _identity?.Key is not SyncKey key || _syncSettings?.Current.LinkOn != true)
        {
            return Array.Empty<byte[]>();
        }

        if (_ownSyncFile is null)
        {
            int generation = _ownSyncFileGeneration;
            try
            {
                SyncFile own = await _sync.BuildOwnFileAsync();
                // Kept only when nothing changed while it was built (a change has already asked for a new one).
                if (generation == _ownSyncFileGeneration)
                {
                    _ownSyncFile = SyncFileCodec.Seal(own, key);
                }
            }
            catch (SqliteException)
            {
                // The history can't be read right now; the next change tries again.
            }
        }

        var files = new List<byte[]>();
        if (_ownSyncFile is not null)
        {
            files.Add(_ownSyncFile);
        }

        files.AddRange(_received.Devices.Select(device => device.Sealed));
        return files;
    }

    public Task<string?> AcceptSyncFileAsync(byte[] sealedBytes)
    {
        return Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (_sync is null || _syncSettings?.Current.LinkOn != true)
            {
                return "Sharing progress over the network link is off on this PC.";
            }

            return await _sync.AcceptSealedAsync(sealedBytes, SyncDeviceCard.MethodLink);
        });
    }

    private void OnSyncDataChanged()
    {
        _ownSyncFile = null;
        _ownSyncFileGeneration++;
        SyncFilesChanged?.Invoke(this, EventArgs.Empty);
    }

    private DateOnly Today()
    {
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_sessions.Now, _sessions.TimeProvider.LocalTimeZone).DateTime);
    }

    private static DateTimeOffset At(DateTime local, TimeZoneInfo zone)
    {
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    public Task<string?> StartAsync(LinkStartRequest request)
    {
        return Dispatcher.UIThread.InvokeAsync(async () =>
            request.CountUp ? await _control.StartWithDefaultAsync() : _control.Start(request.ProfileId, request.Minutes));
    }

    public Task<LinkEndEarlyTerms?> GetEndEarlyTermsAsync()
    {
        return Dispatcher.UIThread.InvokeAsync(() => _control.EndEarlyTerms()).GetTask();
    }

    /// <summary>The daily deep-work goal sent with today's focus (ADR 29); null sends none.</summary>
    public Func<int?>? GoalMinutes { get; set; }

    public Task<LinkDayPlanAnswer> ApplyDayPlanAsync(LinkDayPlanEdit edit)
    {
        return Dispatcher.UIThread.InvokeAsync(() => _planEdits.ApplyAsync(edit));
    }

    public Task<string?> EndEarlyAsync(string reason)
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!_control.TryEnd(reason, out string? notice))
            {
                return (string?)"No session is running.";
            }

            if (notice is not null)
            {
                EndedFromDevice?.Invoke(this, notice);
            }

            return (string?)null;
        }).GetTask();
    }

    private LinkState BuildState()
    {
        // A test session is never shown to linked devices: they see an idle PC (starting one is refused while it runs).
        SessionState state = _sessions.State.IsTest ? SessionState.Idle : _sessions.State;
        IReadOnlyList<FocusProfile> profiles = _configuration.Current.Profiles;
        string? mode = state.Mode switch
        {
            SessionMode.Manual => "manual",
            SessionMode.Pomodoro => "pomodoro",
            SessionMode.Scheduled => "scheduled",
            _ => null,
        };
        string phase = state.Phase switch
        {
            // The link is being deleted (ADR 42): planning reads as focus there and carries nothing new.
            SessionPhase.Focus or SessionPhase.Planning => "focus",
            SessionPhase.ShortBreak => "shortBreak",
            SessionPhase.LongBreak => "longBreak",
            _ => "idle",
        };

        return new LinkState(
            mode,
            phase,
            CountsAsFocus: state.IsEnforcing && !state.IsPlanning && state.Mode != SessionMode.Scheduled,
            state.ProfileId,
            ProfileOptions.NameOf(profiles, state.ProfileId),
            state.FocusStartsAt(_sessions.Now),
            state.FocusEndsAt(_sessions.Now),
            profiles.Select(profile => new LinkProfile(profile.Id, profile.Name)).ToList(),
            _quickSession.CountUpProfileId,
            _sessions.Now,
            state.IsActive ? Tasks?.Invoke() : null,
            state.IsActive ? CurrentTask?.Invoke() : null,
            Comebacks?.Invoke());
    }

    /// <summary>The PC's open come-backs for paired devices (ADR 30); set by the app.</summary>
    public Func<IReadOnlyList<LinkComeback>?>? Comebacks { get; set; }

    /// <summary>Guided sessions (ADR 24): the running session's checklist for the phone; set by the app.</summary>
    public Func<IReadOnlyList<SessionTask>?>? Tasks { get; set; }

    /// <summary>The task or subtask picked to show on the bar (null: the first open task); set by the app.</summary>
    public Func<Guid?>? CurrentTask { get; set; }

    /// <summary>The checklist changed: paired devices get the state again.</summary>
    public void NotifyTasksChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
