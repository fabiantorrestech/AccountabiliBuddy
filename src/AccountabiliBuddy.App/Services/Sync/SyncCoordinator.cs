using System.Text.Json;
using AccountabiliBuddy.App.Services.Backup;
using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// The PC's side of sharing progress between the user's own devices (ADR 22), whatever method carries the files: it
/// builds this PC's sync file (its last 14 days of counted sessions and drift, its day plan and shared settings; only
/// readable fields) and takes in files other devices sealed. A received file is kept per device
/// (<see cref="ReceivedDevicesService"/>). Its day plan goes through the clash rule (<see cref="DayPlanClash"/>): taken or
/// kept without asking when only one side edited, else <see cref="ClashRaised"/>; a plan arriving during a focus session
/// waits until it ends, since the plan is locked then. Its settings apply when they were edited later than this PC's, by
/// this PC's own rules: profiles through <see cref="ConfigurationService.UpdateAsync"/>, so a looser change (or a profile
/// deleted there) waits its delay; settings arriving during a focus session wait until it ends, as they are locked
/// then too. The public calls move to the UI thread themselves.
/// </summary>
public sealed class SyncCoordinator
{
    /// <summary>Why a file was refused when it doesn't open with this group's key (another group's, or damaged).</summary>
    public const string CannotReadReason = "This file can't be read: it's from another device group, or it's damaged.";

    /// <summary>Why a file was refused when it is this PC's own.</summary>
    public const string OwnFileReason = "This is this PC's own sync file.";

    /// <summary>Why a file was refused when it is from a device removed from the group (ADR 54).</summary>
    public const string RemovedDeviceReason = "This file is from a device that was removed from your device group.";

    /// <summary>Why a file was skipped when its header names another device group: a leftover from before a device removal, no problem to show.</summary>
    public const string OtherGroupReason = "This file belongs to another device group.";

    private const string NotInGroupReason = "This PC isn't linked to your other devices yet.";

    // The plan's sync state: its version, when it was last edited, and the newest version it shares with the others.
    private const string PlanStateKey = "sync.dayplan";

    // When the plan last changed, kept for the phone's prompt (DayPlanEditTimeService); read once for a plan edited earlier.
    private const string PlanEditTimeKey = "dayplan.editedAt";

    private readonly SessionController _sessions;
    private readonly ConfigurationService _configuration;
    private readonly IActivityLog _log;
    private readonly OmittedSessionsService _omitted;
    private readonly DeletedUsageService _deleted;
    private readonly PrivacySettingsService _privacy;
    private readonly SleepSettingsService _sleep;
    private readonly DayBarSettingsService _dayBar;
    private readonly GoalService _goal;
    private readonly SqliteJsonDocumentStore _documents;
    private readonly SyncSettingsService _settings;
    private readonly SyncIdentityService _identity;
    private readonly ReceivedDevicesService _received;
    private readonly SyncEditTimesService _editTimes;

    // Plans that arrived during a focus session, by sender, considered when it ends.
    private readonly Dictionary<Guid, WaitingPlan> _waitingPlans = [];

    // Settings that arrived during a focus session, the newest of each, applied when it ends.
    private readonly Dictionary<string, SyncDocument> _waitingDocuments = [];

    private PlanState? _plan;
    private DayPlanClashRequest? _pendingClash;
    private Guid _pendingClashDevice;

    // An incoming plan chosen in the clash prompt during a focus session: taken when it ends.
    private SyncDayPlan? _takeAfterFocus;
    private bool _sessionActive;

    // Saves of the plan state run one after another so an older state never overwrites a newer one.
    private Task _saving = Task.CompletedTask;

    public SyncCoordinator(
        SessionController sessions,
        ConfigurationService configuration,
        IActivityLog log,
        OmittedSessionsService omitted,
        DeletedUsageService deleted,
        PrivacySettingsService privacy,
        SleepSettingsService sleep,
        DayBarSettingsService dayBar,
        GoalService goal,
        SqliteJsonDocumentStore documents,
        SyncSettingsService settings,
        SyncIdentityService identity,
        ReceivedDevicesService received,
        SyncEditTimesService editTimes)
    {
        _sessions = sessions;
        _configuration = configuration;
        _log = log;
        _omitted = omitted;
        _deleted = deleted;
        _privacy = privacy;
        _sleep = sleep;
        _dayBar = dayBar;
        _goal = goal;
        _documents = documents;
        _settings = settings;
        _identity = identity;
        _received = received;
        _editTimes = editTimes;
        _sessionActive = sessions.State.IsRealSession;

        configuration.Changed += (_, _) => OnConfigurationChanged();
        configuration.PendingCancelled += (_, _) => RaiseOwnDataChanged();
        sessions.StateChanged += (_, state) => OnSessionStateChanged(state);
        sleep.Changed += (_, _) => RaiseOwnDataChanged();
        dayBar.Changed += (_, _) => RaiseOwnDataChanged();
        privacy.Changed += (_, _) => RaiseOwnDataChanged();
        omitted.Changed += (_, _) => RaiseOwnDataChanged();
        deleted.Changed += (_, _) => RaiseOwnDataChanged();
        // "Delete my data" (raised off the UI thread): the file then holds no sessions or drift.
        log.AllDeleted += (_, _) => Dispatcher.UIThread.Post(RaiseOwnDataChanged);
        identity.Changed += (_, _) => ForgetGroupWork();
    }

    /// <summary>Removed devices and the rekey file (ADR 54); set by the app. Without it a rekey file is treated as unreadable.</summary>
    public SyncRemovalService? Removal { get; set; }

    /// <summary>The group's past keys (ADR 56), written into this PC's sealed file and merged from the others'; set by the app.</summary>
    public PastKeysService? PastKeys { get; set; }

    /// <summary>
    /// The receiver-local record behind this PC's receipts (ADR 86, SYNC1b): what arrived by which method, why a read failed, what each part came
    /// to. Recording never raises <see cref="OwnDataChanged"/> (receiving a file never triggers a write). Set by the app.
    /// </summary>
    public SyncReceiptsStore? Receipts { get; set; }

    /// <summary>This PC's own puts per method, times only (SYNC1b); set by the app.</summary>
    public SyncSendLog? SendLog { get; set; }

    /// <summary>The files the methods hold but this PC does not take in (SYNC1b); set by the app.</summary>
    public SyncUnreadableFiles? Unreadable { get; set; }

    /// <summary>The rolling sync log (counts and times only, SYNC1b); set by the app.</summary>
    public SyncLog? SyncLogger { get; set; }

    /// <summary>Whether this PC's task store can take parts in (open and readable); null counts as ready. Set by the app.</summary>
    public Func<bool>? TasksReady { get; set; }

    /// <summary>Whether this PC's goal store can take parts in (migrated and readable); null counts as ready. Set by the app.</summary>
    public Func<bool>? GoalsReady { get; set; }

    /// <summary>The day plan was changed here and on another device before they synced: ask which to keep.</summary>
    public event EventHandler<DayPlanClashRequest>? ClashRaised;

    /// <summary>
    /// This PC's own data changed: a session started or ended, the history changed, the plan or a shared setting was
    /// edited. A received file raises it only when it changed what this PC's own file says (a plan or setting taken, the
    /// plan now shared, an edit time adopted), never merely for arriving, so two devices never answer each other forever.
    /// </summary>
    /// <summary>Guided sessions (ADR 24): a session's checklist by its start, for this PC's file; set by the app.</summary>
    public Func<DateTimeOffset, IReadOnlyList<SessionTask>?>? TasksOf { get; set; }

    public event EventHandler? OwnDataChanged;

    /// <summary>Loads the plan's sync state, before the first file is built or received.</summary>
    public void LoadBlocking()
    {
        string version = DayPlanVersion.Of(_configuration.Current.DayPlan);
        PlanState? stored = LoadBlocking<PlanState>(PlanStateKey);
        if (stored is not null && stored.Version == version)
        {
            _plan = stored;
            return;
        }

        if (stored is not null && DayPlanVersion.IsEarlierSchemaVersionOf(stored.Version, _configuration.Current.DayPlan))
        {
            // First start after the v12 or v13 upgrade (ADR 47): the plan is the one an earlier schema's version was stamped on, migrated
            // unchanged, so it gets the new version with its edit time and shared/edited state as they were: no edit.
            _plan = stored with { Version = version, BasedOn = stored.BasedOn == stored.Version ? version : stored.BasedOn };
            SavePlanState();
            return;
        }

        if (stored is null && version == DayPlanVersion.Of(DayPlan.Default))
        {
            // Nobody edited it (a new device): it gives way to any edited plan.
            _plan = new PlanState(version, DateTimeOffset.UnixEpoch, version);
        }
        else
        {
            // Edited where this wasn't watching (before this PC shared, or a restore): an edit of what it shared before.
            PlanEditTime? editTime = LoadBlocking<PlanEditTime>(PlanEditTimeKey);
            DateTimeOffset editedAt = editTime?.Version == version && editTime.EditedAt is DateTimeOffset known ? known : _sessions.Now;
            _plan = new PlanState(version, editedAt, stored?.BasedOn);
        }

        SavePlanState();
    }

    /// <summary>The history changed by hand: a session added, edited or deleted (ADR 20). "Delete my data" comes through <see cref="IActivityLog.AllDeleted"/>.</summary>
    public void NotifyHistoryChanged()
    {
        RaiseOwnDataChanged();
    }

    /// <summary>
    /// This PC's sync file as of now: its card, the counted sessions and drift of the last 14 days (none while Activity
    /// history is off, ADR 15), the plan and the shared settings that have an edit time.
    /// </summary>
    /// <exception cref="SqliteException">The history can't be read right now.</exception>
    public Task<SyncFile> BuildOwnFileAsync()
    {
        return RunInline ? BuildOwnFileOnUiThreadAsync() : Dispatcher.UIThread.InvokeAsync(BuildOwnFileOnUiThreadAsync);
    }

    /// <summary>
    /// Runs the public calls on the caller's thread instead of moving them to the UI thread. For check programs only, which have no UI
    /// thread; the app never sets it.
    /// </summary>
    public bool RunInline { get; set; }

    /// <summary>Takes in a file another device sealed, carried by <paramref name="method"/> (see <see cref="SyncDeviceCard"/>).</summary>
    /// <param name="fileName">
    /// The file's name on a folder, Drive or the relay (null for a QR code or an imported file): it tells whose file it is when it can't be
    /// read (the names are <see cref="SyncFolderService.FileNameFor"/> of a device) and keys the troubleshooting list. Never logged or shown.
    /// </param>
    /// <returns>Null when accepted (or already held); otherwise why not, e.g. <see cref="CannotReadReason"/>.</returns>
    public Task<string?> AcceptSealedAsync(byte[] sealedBytes, string method, string? fileName = null)
    {
        return RunInline ? AcceptGuardedAsync(sealedBytes, method, fileName) : Dispatcher.UIThread.InvokeAsync(() => AcceptGuardedAsync(sealedBytes, method, fileName));
    }

    // Every way a file arrives (folder, Drive, relay, file, QR) comes through here. A file that opens but is malformed in a
    // way no check foresaw is skipped as unreadable (the same bytes would fail the same way, so the callers don't retry it);
    // it never throws out of a sync loop. A storage failure (SqliteException) still goes to the caller, who reads it again
    // later. Nothing of the file's content is logged or put in the reason.
    private async Task<string?> AcceptGuardedAsync(byte[] sealedBytes, string method, string? fileName)
    {
        try
        {
            return await AcceptOnUiThreadAsync(sealedBytes, method, fileName);
        }
        catch (Exception exception) when (exception is not SqliteException and not OperationCanceledException)
        {
            return CannotReadReason;
        }
    }

    /// <summary>
    /// The user's answer to <paramref name="request"/>: "Keep the one I was using" keeps this PC's plan (the other
    /// device then takes it); "Use the latest" takes the plan edited last. A request that was replaced or already
    /// answered changes nothing. A plan to take during a focus session is taken when it ends.
    /// </summary>
    public Task ResolveClashAsync(DayPlanClashRequest request, DayPlanClashChoice choice)
    {
        return Dispatcher.UIThread.InvokeAsync(() => ResolveOnUiThreadAsync(request, choice));
    }

    private async Task<SyncFile> BuildOwnFileOnUiThreadAsync()
    {
        using IDisposable timing = AccountabiliBuddy.App.Services.TickDiag.Measure("sync file build");
        TimeZoneInfo zone = _sessions.TimeProvider.LocalTimeZone;
        DateTimeOffset now = _sessions.Now;
        bool historyOff = !_privacy.Current.ActivityLogEnabled;
        var card = new SyncDeviceCard(
            _identity.DeviceId, _identity.DeviceName, SyncDeviceCard.KindPc, BackupService.AppVersion, now, Methods(), historyOff, Seen(),
            PublicKey: _identity.DeviceKey.PublicKeyBase64);

        IReadOnlyList<SessionRecord> records = [];
        IReadOnlyList<DriftStretch> drift = [];
        DateTimeOffset? oldestSessionStart = null;
        if (!historyOff)
        {
            // The 14 days and the day before them (DST included), and everything kept, to know whether anything older is.
            DateTimeOffset from = now - SyncMerge.FileSpan - TimeSpan.FromDays(2);
            DateTimeOffset to = now + TimeSpan.FromDays(1);
            // Off the UI thread: Microsoft.Data.Sqlite's async calls run synchronously.
            IReadOnlyList<SessionEvent> events = await Task.Run(() => _log.GetSessionEventsAsync(now - SyncMerge.KeptFor - TimeSpan.FromDays(1), to));
            oldestSessionStart = events.Count > 0 ? events[0].At : null;
            records = WithGaps(SessionHistoryBuilder.Build(events.Where(sessionEvent => sessionEvent.At >= from)));
            List<TimeRange> leftOut = records
                .Where(record => _omitted.IsOmitted(record.StartedAt) || record.Mode == SessionMode.Scheduled)
                .Select(record => new TimeRange(record.StartedAt, record.EndedAt ?? now))
                .ToList();
            IReadOnlyList<UsageDeletion> deletions = _deleted.Parts;
            drift = await Task.Run(() => _log.GetDriftAsync(from, to, leftOut, deletions));
        }

        IReadOnlyList<FocusProfile> profiles = _configuration.Current.Profiles;
        SyncNoise? noise = Noise is null ? null : await Noise();
        SyncFile built = SyncFileBuilder.Build(card, records, drift, _omitted.IsOmitted, id => ProfileOptions.NameOf(profiles, id),
            _sessions.State.IsRealSession, OwnPlan(), SharedDocuments(), now, zone, oldestSessionStart, TasksOf,
            _sessions.State.IsPlanning ? _sessions.State.PhaseEndsAt : null) with { Comebacks = ShareTaskLists() ? Comebacks?.Invoke() : null, Lunch = Lunch?.Invoke(), Acks = Acks?.Invoke() is { Count: > 0 } acks ? acks : null, Running = RunningNow(profiles), Events = Events?.Invoke(),
                Pickups = PcPickups?.PickupsForOtherPcs(), InputRuns = PcPickups?.InputRunsToday(),
                ReminderActions = ReminderActions?.Invoke() is { Count: > 0 } actions ? actions : null,
                Blockers = Blockers?.Invoke(), Beeminder = Beeminder?.Invoke(),
                ActionResults = ActionResults?.Invoke() is { Count: > 0 } results ? results : null,
                EndEarly = EndEarlyOffer?.Invoke(), GoalMinutes = GoalMinutes?.Invoke(),
                // ADR 42: always, read-only, for the phone's start picker (the profiles document travels only once edited).
                ProfileOptions = profiles.Select(profile => new SyncProfileOption(profile.Id, profile.Name)).ToList(),
                DefaultProfileId = CountUpProfileId?.Invoke(), Noise = noise,
                // ADR 48, 50: the shared lists, each with its tombstones; sealed like everything in the file.
                // ADR 71: a PC that doesn't share its task lists sends none (read by the others as "keeps no shared lists").
                NextTasks = ShareTaskLists() ? NextTasks?.Invoke() : null,
                Parked = ShareTaskLists() ? Parked?.Invoke() : null,
                Later = ShareTaskLists() ? Later?.Invoke() : null,
                // ADR 56: the past keys, for opening backups; sealed with the current key like everything else.
                PastKeys = PastKeys?.ForFile(),
                // This PC's task categories, always listed (empty when it has none), sealed like everything else.
                Categories = ShareTaskLists() ? Categories?.Invoke() : null,
                // ADR 65: today's work ended early, times only (never the reason).
                WorkEnded = WorkEnded?.Invoke(),
                // ADR 66: the waits running in the background here, times only (never the reason), for the phone's countdown and Cancel.
                ArmedEnds = ArmedEnds?.Invoke() is { Count: > 0 } armed ? armed : null,
                // ADR 68: the guidance mode this PC chose, mode and time only; null for a default nobody chose.
                Guidance = Guidance?.Invoke(),
                // ADR 86: what this PC holds and applied of the other devices' files; none while it is alone in its group (works alone).
                Receipts = Receipts?.ForFile(_identity.DeviceId, _received.Active.Select(device => device.File.Device.DeviceId).ToHashSet()) };
        // ADR 72: the task history, settle records, Auto-saved list and handovers, while task lists are shared (ADR 71).
        SyncFile decorated = ShareTaskLists() && DecorateTaskLists is { } decorate ? decorate(built) : built;
        // ADR 76: the one task list's parts, and the old fields rebuilt from it for older apps (they replace the lists' own).
        SyncFile withTasks = ShareTaskLists() && TaskModelParts is { } parts ? parts(decorated) : decorated;
        // ADR 79: the goal lists follow the same switch (none while the goals aren't ready).
        return ShareTaskLists() && GoalParts is { } goalParts ? goalParts(withTasks) : withTasks;
    }

    /// <summary>ADR 79 (GS3a): adds the five goal parts to this PC's file (<see cref="GoalListSync.Decorate"/>). Set by the app.</summary>
    public Func<SyncFile, SyncFile>? GoalParts { get; set; }

    /// <summary>ADR 79 (GS3a): takes another device's goal parts in (<see cref="GoalListSync.ReceiveAsync"/>). Set by the app.</summary>
    public Func<SyncFile, Task<bool>>? MergeGoals { get; set; }

    /// <summary>
    /// The copy of a received file this PC keeps (<see cref="ReceivedDevicesService"/>, stored in the clear): without what is merged into
    /// this PC's own sealed stores (the shared lists, ADR 48; the past keys, ADR 56; ADR 76's task parts and the account's Tasks settings;
    /// ADR 79's goal parts and the goal history marker), so their text is never kept a second time in the clear. <paramref name="steps"/>
    /// is the step count to keep (a QR file's, which carries none, keeps the stored one).
    /// </summary>
    public static SyncFile KeptCopy(SyncFile file, SyncSteps? steps)
    {
        return file with
        {
            NextTasks = null, Parked = null, Later = null, PastKeys = null, Steps = steps, Tasks = null, Labels = null, Views = null, TaskLog = null,
            TaskAccountSettings = null, Goals = null, GoalAreas = null, GoalEntries = null, GoalReminders = null, GoalReviews = null, GoalYearsMerged = null,
        };
    }

    /// <summary>
    /// ADR 76: adds the task list's parts (tasks, labels, views, History) and the old task fields rebuilt from it to this PC's file.
    /// Set by the app once the task list is the source; null before.
    /// </summary>
    public Func<SyncFile, SyncFile>? TaskModelParts { get; set; }

    /// <summary>
    /// ADR 76: reads another device's file into the task list (its task parts, or its old fields), in place of the old lists'
    /// merges. Set by the app once the task list is the source; null before.
    /// </summary>
    public Func<SyncFile, Task<bool>>? MergeTaskModel { get; set; }

    /// <summary>ADR 72: adds the task history, settle records, Auto-saved list and handovers to this PC's file. Set by the app.</summary>
    public Func<SyncFile, SyncFile>? DecorateTaskLists { get; set; }

    /// <summary>ADR 72: takes those parts in from another device's file (only while task lists are shared). Set by the app.</summary>
    public Func<SyncFile, Task>? ReceiveTaskLists { get; set; }

    /// <summary>Distracted stretches marked in History, folded into the records; read again when they change. Set by the app.</summary>
    public SessionGapsService? Gaps
    {
        get => _gaps;
        set
        {
            _gaps = value;
            if (value is not null)
            {
                value.Changed += (_, _) => RaiseOwnDataChanged();
            }
        }
    }

    private SessionGapsService? _gaps;

    private IReadOnlyList<SessionRecord> WithGaps(IReadOnlyList<SessionRecord> records) => _gaps?.Apply(records) ?? records;

    /// <summary>The PC's open come-backs for its sync file (ADR 30), the form an older phone reads; set by the app.</summary>
    public Func<IReadOnlyList<SyncComeback>?>? Comebacks { get; set; }

    /// <summary>The next session's task list with its tombstones for this PC's sync file (ADR 48); set by the app.</summary>
    public Func<IReadOnlyList<SyncNextTask>?>? NextTasks { get; set; }

    /// <summary>The come-backs with their tombstones for this PC's sync file (ADR 48); set by the app.</summary>
    public Func<IReadOnlyList<SyncParked>?>? Parked { get; set; }

    /// <summary>The Later list with its tombstones for this PC's sync file (ADR 50); set by the app.</summary>
    public Func<IReadOnlyList<SyncLaterItem>?>? Later { get; set; }

    /// <summary>This PC's task categories (name and colour) for its sync file, so the other devices can offer them; set by the app.</summary>
    public Func<IReadOnlyList<SyncCategory>?>? Categories { get; set; }

    /// <summary>
    /// Merges another device's next-session list into this PC's (ADR 48); true when it changed. A merge that changed it
    /// raises the store's own change event, which writes this PC's file again; set by the app.
    /// </summary>
    public Func<IReadOnlyList<SyncNextTask>, bool, Task<bool>>? MergeNextTasks { get; set; }

    /// <summary>Merges another device's come-backs into this PC's (ADR 48); true when they changed; set by the app.</summary>
    public Func<IReadOnlyList<SyncParked>, Task<bool>>? MergeParked { get; set; }

    /// <summary>Merges another device's Later list into this PC's (ADR 50); true when it changed; set by the app.</summary>
    public Func<IReadOnlyList<SyncLaterItem>, Task<bool>>? MergeLater { get; set; }

    // The running session for the phone's countdown (ADR 26); old scheduled blocks don't count.
    private SyncRunning? RunningNow(IReadOnlyList<FocusProfile> profiles)
    {
        SessionState state = _sessions.State;
        // A test session is never sent.
        if (!state.IsRealSession || state.Mode == SessionMode.Scheduled || state.PhaseStartedAt is not DateTimeOffset started)
        {
            return null;
        }

        // Planning (not focus time): its own start and end go in the optional planning fields; the phase fields hold the focus
        // phase it leads to (planning's end, and the focus length after it), which is all a build that doesn't know planning reads.
        DateTimeOffset? planningStartedAt = state.IsPlanning ? started : null;
        DateTimeOffset? planningEndsAt = state.IsPlanning ? state.PhaseEndsAt : null;
        return new SyncRunning(ProfileOptions.NameOf(profiles, state.ProfileId), state.FocusStartsAt(_sessions.Now) ?? started, state.FocusEndsAt(_sessions.Now),
            // ADR 77: a step away with the timer running goes out as no away, so an older phone keeps counting down to the real end.
            state.Phase is SessionPhase.ShortBreak or SessionPhase.LongBreak, state.AwayTimerRuns ? null : state.AwaySince,
            CurrentTask?.Invoke(), state.ProfileId, CountUpProfileId?.Invoke(), planningStartedAt, planningEndsAt, GoingOffAt);
    }

    /// <summary>ADR 60: Windows said it is shutting down, with a session running; goes in this PC's last file. Null otherwise.</summary>
    public DateTimeOffset? GoingOffAt { get; set; }

    /// <summary>ADR 42: the task or subtask picked to show on the bar while a session runs (null: none); set by the app.</summary>
    public Func<Guid?>? CurrentTask { get; set; }

    /// <summary>ADR 42: the profile "Quick Start with Default" starts, null when not made yet; set by the app.</summary>
    public Func<Guid?>? CountUpProfileId { get; set; }

    /// <summary>ADR 42: the answers to the phone's actions still in its file; set by the app.</summary>
    public Func<IReadOnlyList<SyncActionResult>>? ActionResults { get; set; }

    /// <summary>ADR 42: the open end-early offer, null for none; set by the app.</summary>
    public Func<SyncEndEarlyOffer?>? EndEarlyOffer { get; set; }

    /// <summary>ADR 36, 42: the noise player's state, null when it has no sound files; set by the app.</summary>
    public Func<Task<SyncNoise?>>? Noise { get; set; }

    /// <summary>ADR 29, 42: the daily focus goal in minutes; set by the app.</summary>
    public Func<int?>? GoalMinutes { get; set; }

    /// <summary>True while a lower received goal has to wait (the goal card's lowering guard); set by the app.</summary>
    public Func<bool>? GoalLowerWaits { get; set; }

    /// <summary>This PC's pickups during the other PCs' sessions and today's input stretches (design K); set by the app.</summary>
    public PcPickupsService? PcPickups { get; set; }

    /// <summary>The visible calendars' events for the other devices (ADR 26); set by the app.</summary>
    public Func<JsonElement?>? Events { get; set; }

    /// <summary>The phone's actions this PC applied (ADR 26), acknowledged in its file; set by the app.</summary>
    public Func<IReadOnlyList<Guid>>? Acks { get; set; }

    /// <summary>This PC's Cold Turkey blocks and whether each is on (ADR 37); null without Cold Turkey. Set by the app.</summary>
    public Func<IReadOnlyList<SyncBlocker>?>? Blockers { get; set; }

    /// <summary>The derived summary of the user's Beeminder goals (ADR 43); null without a token. Set by the app.</summary>
    public Func<IReadOnlyList<SyncBeeminderGoal>?>? Beeminder { get; set; }

    /// <summary>Bedtime reminders dismissed or snoozed on this PC in the last day (ADR 38); set by the app.</summary>
    public Func<IReadOnlyList<SyncReminderAction>?>? ReminderActions { get; set; }

    /// <summary>Today's flexible lunch once started on this PC or taken from another device (schema v8); set by the app.</summary>
    public Func<SyncLunch?>? Lunch { get; set; }

    /// <summary>Today's work ended early (ADR 65), ended here or taken from another PC; times only. Set by the app.</summary>
    public Func<SyncWorkEnded?>? WorkEnded { get; set; }

    /// <summary>
    /// ADR 71: this PC shares its task lists (tasks, Later, come-backs, task categories) with the other devices; set by the app. Off,
    /// none are sent or taken.
    /// </summary>
    public Func<bool> ShareTaskLists { get; set; } = () => true;

    /// <summary>The guidance mode chosen here or taken from another device (ADR 68), mode and time only; set by the app.</summary>
    public Func<SyncGuidance?>? Guidance { get; set; }

    /// <summary>The armed ends running here (ADR 66), times only; set by the app.</summary>
    public Func<IReadOnlyList<SyncArmedEnd>>? ArmedEnds { get; set; }

    private async Task<string?> AcceptOnUiThreadAsync(byte[] sealedBytes, string method, string? fileName = null)
    {
        if (_identity.Key is not SyncKey key)
        {
            return NotInGroupReason;
        }

        // The rekey file (ADR 54) travels with the devices' files, under a name shaped like theirs.
        if (RekeyCodec.IsRekey(sealedBytes))
        {
            return Removal is null ? CannotReadReason : await Removal.AcceptRekeyAsync(sealedBytes, key);
        }

        if (SyncFileCodec.IsOtherGroups(sealedBytes, key))
        {
            NoteUnreadable(key, method, fileName, SyncUnreadableFile.OtherGroup);
            return OtherGroupReason;
        }

        SyncFileOpenResult opened = SyncFileCodec.TryOpen(sealedBytes, key);
        if (opened.File is not SyncFile file)
        {
            NoteUnreadable(key, method, fileName, opened.Code ?? SyncFileOpenFailure.Damaged.ToString().ToLowerInvariant());
            return CannotReadReason;
        }

        SyncDeviceCard sender = file.Device;
        if (sender.DeviceId == _identity.DeviceId)
        {
            return OwnFileReason;
        }

        // A removed device's file is refused (ADR 54). One that opens with the group's NEW key, written after the removal, can
        // only come from a device that was linked again: it is a member once more.
        if (Removal?.RemovedAs(sender.DeviceId) is RemovedDevice removed)
        {
            if (sender.WrittenAt <= removed.RemovedAt)
            {
                return RemovedDeviceReason;
            }

            await Removal.ClearRemovedAsync(sender.DeviceId);
        }

        // ADR 86: the method this file arrived by is recorded before any "already has newer data" decision, so a copy that came late (the
        // relay delivered first, Drive after) still proves the method works. It reads fine now: it leaves the troubleshooting list.
        if (fileName is not null)
        {
            Unreadable?.Clear(method, fileName);
        }

        Receipts?.RecordArrival(sender.DeviceId, method, sender.WrittenAt);

        ReceivedDevice? stored = _received.Devices.FirstOrDefault(device => device.File.Device.DeviceId == sender.DeviceId);
        if (stored is not null && sender.WrittenAt < stored.File.Device.WrittenAt)
        {
            return $"This PC already has newer data from {sender.Name}.";
        }

        // The shared lists (ADR 48) are merged into this PC's own, encrypted stores below; the copy kept of the sender's file
        // doesn't hold them, so their text isn't kept a second time, in the clear (the sealed bytes it passes on are sealed).
        // The past keys (ADR 56) are merged into their own encrypted store and left out of the copy kept here too.
        // So are ADR 76's task parts and the account's Tasks settings (the picked calendar id can be the account's address), and ADR 79's
        // goal parts (KeptCopy).
        // A QR file never carries steps, which reads the same as "switched off": it keeps the stored counts rather than wiping them.
        SyncSteps? steps = method == SyncDeviceCard.MethodQr ? file.Steps ?? stored?.File.Steps : file.Steps;
        await _received.AcceptAsync(KeptCopy(file, steps), method, sealedBytes);
        if (PastKeys is not null)
        {
            await PastKeys.MergeAsync(file.PastKeys);
        }

        // ADR 86: what each part of this file came to here (taken, held back, off, not in the file, refused): kept in the receiver-local record
        // and sent back in this PC's next file. A part that threw is refused, never lost to the caller.
        var outcome = new Dictionary<string, string>
        {
            ["sessions"] = sender.HistoryOff ? SyncReceiptParts.Absent : SyncReceiptParts.Taken,
            ["steps"] = file.Steps is null ? SyncReceiptParts.Absent : SyncReceiptParts.Taken,
            ["lunch"] = file.Lunch is null ? SyncReceiptParts.Absent : SyncReceiptParts.Taken,
            ["actions"] = file.Actions is null ? SyncReceiptParts.Absent : SyncReceiptParts.Taken,
        };
        outcome["dayPlan"] = file.DayPlan is SyncDayPlan plan ? await Guarded(() => ConsiderPlanAsync(plan, sender.DeviceId, sender.Name, sender.WrittenAt)) : SyncReceiptParts.Absent;
        outcome["settings"] = file.Documents.Count == 0
            ? SyncReceiptParts.Absent
            : await Guarded(async () => await ReceiveDocumentsAsync(file.Documents) ? SyncReceiptParts.Held : SyncReceiptParts.Taken);
        foreach ((string code, string state) in await ReceiveSharedListsAsync(file))
        {
            outcome[code] = state;
        }

        Receipts?.RecordParts(sender.DeviceId, sender.WrittenAt, outcome);
        CountParts(method, outcome);
        return null;
    }

    // What the parts of the accepted files came to, per method, until the method's pass is logged (TakePartCounts).
    private readonly Dictionary<string, (int Taken, int Held, int Refused)> _partCounts = [];

    private void CountParts(string method, IReadOnlyDictionary<string, string> outcome)
    {
        (int taken, int held, int refused) = _partCounts.GetValueOrDefault(method);
        _partCounts[method] = (
            taken + outcome.Values.Count(state => state == SyncReceiptParts.Taken),
            held + outcome.Values.Count(state => state == SyncReceiptParts.Held),
            refused + outcome.Values.Count(state => state == SyncReceiptParts.Refused));
    }

    /// <summary>The parts taken, held back and refused by the files accepted by <paramref name="method"/> since the last call; the count starts again.</summary>
    public (int Taken, int Held, int Refused) TakePartCounts(string method)
    {
        (int, int, int) counts = _partCounts.GetValueOrDefault(method);
        _partCounts.Remove(method);
        return counts;
    }

    /// <summary>This PC's file reached <paramref name="method"/> (SYNC1b): the send log and the sync log, times and counts only.</summary>
    public void NoteSent(string method, DateTimeOffset writtenAt, int milliseconds)
    {
        SendLog?.Record(method, writtenAt, _sessions.Now);
        SyncLogger?.Pass(method, SyncLogDirection.Send, files: 1, merged: 0, held: 0, skipped: 0, unreadable: 0, milliseconds);
    }

    /// <summary>A read pass over <paramref name="method"/> ended (SYNC1b): one line of counts in the sync log.</summary>
    public void NoteReadPass(string method, int files, int skipped, int unreadable, int milliseconds)
    {
        (int taken, int held, int refused) = TakePartCounts(method);
        SyncLogger?.Pass(method, SyncLogDirection.Read, files, taken, held, skipped, unreadable, milliseconds, refused);
    }

    private static async Task<string> Guarded(Func<Task<string>> part)
    {
        try
        {
            return await part();
        }
        catch (Exception ex) when (ex is not SqliteException and not OperationCanceledException)
        {
            return SyncReceiptParts.Refused;
        }
    }

    /// <summary>
    /// A file the method holds did not open (or is an old group's leftover): the troubleshooting list gets a line, and when the file's name says
    /// whose it is, that device's row of the receiver-local record gets the reason (ADR 86 "failed"). Nothing is logged here; the name is only
    /// matched against <see cref="SyncFolderService.FileNameFor"/> of the devices this PC knows.
    /// </summary>
    private void NoteUnreadable(SyncKey key, string method, string? fileName, string reason)
    {
        if (fileName is null)
        {
            return;
        }

        Guid? device = DeviceOfName(key, fileName);
        Unreadable?.Report(method, fileName, reason, device, IsOwnStale(fileName));
        if (device is Guid owner && reason != SyncUnreadableFile.OtherGroup)
        {
            Receipts?.RecordFailure(owner, method, reason, _sessions.Now);
        }
    }

    /// <summary>The device a file name is <see cref="SyncFolderService.FileNameFor"/> of (in the current group), among those this PC knows; null when none.</summary>
    public Guid? DeviceOfName(SyncKey key, string fileName)
    {
        foreach (Guid id in KnownDeviceIds())
        {
            if (string.Equals(SyncFolderService.FileNameFor(key.GroupId, id), fileName, StringComparison.OrdinalIgnoreCase))
            {
                return id;
            }
        }

        return null;
    }

    private IEnumerable<Guid> KnownDeviceIds()
    {
        return _received.Devices.Select(device => device.File.Device.DeviceId)
            .Concat(Removal?.Removed.Select(removed => removed.DeviceId) ?? [])
            .Distinct();
    }

    /// <summary>
    /// A name that is this PC's own file of an old group (<see cref="PastKeys"/> knows the group ids it left behind): stale, and this PC's to
    /// remove. A current-group name is never stale here.
    /// </summary>
    public bool IsOwnStale(string fileName)
    {
        if (PastKeys?.ForFile() is not { } past)
        {
            return false;
        }

        // The current group's own name is never stale, even if the past keys list holds the current group's id (a device linked back in).
        string? current = _identity.Key?.GroupId;
        return past.Any(old => old.GroupId != current
            && string.Equals(SyncFolderService.FileNameFor(old.GroupId, _identity.DeviceId), fileName, StringComparison.OrdinalIgnoreCase));
    }

    private static readonly TimeSpan FutureWrittenAtLimit = TimeSpan.FromSeconds(60);

    // ADR 48, 50: the next session's tasks, the come-backs and the Later list merge item by item on every accepted file, whatever carried it.
    // A file without them (a QR file, an older app) leaves this PC's lists alone.
    // A file written more than a minute ahead of this PC's clock (the sender's clock runs fast) is not taken for its lists; a
    // later file from it, once the clocks agree, is. Item times past 2 minutes ahead are clamped by the merge itself.
    // Returns what each shared part came to for the receipt (ADR 86): off (the switch), refused (a clock too far ahead, a store that is closed,
    // a part that threw), absent (not in the file), taken.
    private async Task<IReadOnlyDictionary<string, string>> ReceiveSharedListsAsync(SyncFile file)
    {
        bool hasTasks = file.Tasks is not null || file.Labels is not null || file.Views is not null || file.TaskLog is not null || file.TaskAccountSettings is not null;
        bool hasOld = file.NextTasks is not null || file.Parked is not null || file.Later is not null;
        bool hasGoals = file.Goals is not null || file.GoalAreas is not null || file.GoalEntries is not null || file.GoalReminders is not null || file.GoalReviews is not null;
        var states = new Dictionary<string, string>();

        // ADR 71: a PC that doesn't share its task lists doesn't take the others' either.
        if (!ShareTaskLists())
        {
            states["tasks"] = hasTasks ? SyncReceiptParts.Off : SyncReceiptParts.Absent;
            states["oldLists"] = hasOld ? SyncReceiptParts.Off : SyncReceiptParts.Absent;
            states["goals"] = hasGoals ? SyncReceiptParts.Off : SyncReceiptParts.Absent;
            states["goalHistory"] = hasGoals ? SyncReceiptParts.Off : SyncReceiptParts.Absent;
            return states;
        }

        if (file.Device.WrittenAt > _sessions.Now + FutureWrittenAtLimit)
        {
            states["tasks"] = hasTasks ? SyncReceiptParts.Refused : SyncReceiptParts.Absent;
            states["oldLists"] = hasOld ? SyncReceiptParts.Refused : SyncReceiptParts.Absent;
            states["goals"] = hasGoals ? SyncReceiptParts.Refused : SyncReceiptParts.Absent;
            states["goalHistory"] = hasGoals ? SyncReceiptParts.Refused : SyncReceiptParts.Absent;
            return states;
        }

        bool tasksOpen = TasksReady?.Invoke() ?? true;
        bool goalsOpen = GoalsReady?.Invoke() ?? true;
        string tasksState = SyncReceiptParts.Taken;
        try
        {
            await MergeSharedTaskPartsAsync(file);
        }
        catch (Exception ex) when (ex is not SqliteException and not OperationCanceledException)
        {
            tasksState = SyncReceiptParts.Refused;
        }

        string goalsState = SyncReceiptParts.Taken;
        try
        {
            // ADR 79: the goal lists, under the same switch; a file without them (an older app, a QR file) leaves the goals alone.
            if (MergeGoals is { } mergeGoals)
            {
                await mergeGoals(file);
            }
        }
        catch (Exception ex) when (ex is not SqliteException and not OperationCanceledException)
        {
            goalsState = SyncReceiptParts.Refused;
        }

        states["tasks"] = !hasTasks ? SyncReceiptParts.Absent : tasksOpen && MergeTaskModel is not null ? tasksState : SyncReceiptParts.Refused;
        states["oldLists"] = !hasOld ? SyncReceiptParts.Absent : tasksOpen ? tasksState : SyncReceiptParts.Refused;
        states["goals"] = !hasGoals ? SyncReceiptParts.Absent : goalsOpen && MergeGoals is not null ? goalsState : SyncReceiptParts.Refused;
        states["goalHistory"] = states["goals"];
        return states;
    }

    private async Task MergeSharedTaskPartsAsync(SyncFile file)
    {
        if (MergeTaskModel is { } mergeModel)
        {
            // ADR 76: one file at a time into the task list (an older app's moves pair within one file).
            await mergeModel(file with
            {
                NextTasks = file.NextTasks is { } next ? CleanNextTasks(next) : null,
                Parked = file.Parked is { } parkedItems ? CleanParked(parkedItems) : null,
                Later = file.Later is { } laterItems ? CleanLater(laterItems) : null,
            });
        }
        else if (file.NextTasks is { } nextTasks && MergeNextTasks is not null)
        {
            // A file with a "later" list (even an empty one) comes from a writer that knows ADR 50's fields; one without comes
            // from an older app, whose tasks inherit what this PC holds for them.
            await MergeNextTasks(CleanNextTasks(nextTasks), file.Later is not null);
        }

        if (MergeTaskModel is null && file.Parked is { } parked && MergeParked is not null)
        {
            await MergeParked(CleanParked(parked));
        }

        if (MergeTaskModel is null && file.Later is { } later && MergeLater is not null)
        {
            await MergeLater(CleanLater(later));
        }

        if (ReceiveTaskLists is { } receive)
        {
            await receive(file);
        }
    }

    // A sealed file is trusted for its key, not its shape: an item that is null, or a title, text or step list that is null
    // (a hand-made or damaged file), must not throw out of the receive loop. A null item is dropped, a null text becomes empty,
    // as the phone does per item.
    private static List<SyncNextTask> CleanNextTasks(IEnumerable<SyncNextTask?> items)
    {
        return items
            .OfType<SyncNextTask>()
            .Select(task => task with
            {
                Title = task.Title ?? string.Empty,
                Subtasks = task.Subtasks?.OfType<SessionSubtask>().Select(step => step with { Title = step.Title ?? string.Empty }).ToList(),
            })
            .ToList();
    }

    private static List<SyncLaterItem> CleanLater(IEnumerable<SyncLaterItem?> items)
    {
        return items
            .OfType<SyncLaterItem>()
            .Select(item => item with
            {
                Title = item.Title ?? string.Empty,
                Subtasks = item.Subtasks?.OfType<SessionSubtask>().Select(step => step with { Title = step.Title ?? string.Empty }).ToList(),
            })
            .ToList();
    }

    private static List<SyncParked> CleanParked(IEnumerable<SyncParked?> items)
    {
        return items
            .OfType<SyncParked>()
            .Select(item => item with { Text = item.Text ?? string.Empty })
            .ToList();
    }

    // Returns the plan's receipt state (ADR 86): held while it waits for a session to end or for the user's answer, refused when it can't be
    // read, taken otherwise (taken, kept, or the same).
    private async Task<string> ConsiderPlanAsync(SyncDayPlan incoming, Guid fromId, string fromName, DateTimeOffset writtenAt)
    {
        // The plan is locked during focus: it waits until the session ends.
        if (_sessions.IsEnforcing)
        {
            _waitingPlans[fromId] = new WaitingPlan(incoming, fromName, writtenAt);
            return SyncReceiptParts.Held;
        }

        if (ReadPlan(incoming) is not DayPlan incomingPlan)
        {
            return SyncReceiptParts.Refused;
        }

        SyncDayPlan local = OwnPlan();
        DayPlanSyncDecision decision = DayPlanClash.Decide(local, incoming);
        if (decision != DayPlanSyncDecision.Ask && _pendingClash is not null && _pendingClashDevice == fromId)
        {
            // A newer file from that device settled it.
            _pendingClash = null;
        }

        switch (decision)
        {
            case DayPlanSyncDecision.Same:
                if (local.HasLocalEdits())
                {
                    // Its file now says the plan is shared (once: it has no local edits after this).
                    SetPlanState(local.Shared());
                    RaiseOwnDataChanged();
                }

                break;
            case DayPlanSyncDecision.TakeIncoming:
                await TakePlanAsync(incoming);
                return _takeAfterFocus is not null ? SyncReceiptParts.Held : SyncReceiptParts.Taken;
            case DayPlanSyncDecision.Ask:
                Ask(local, incoming, incomingPlan, fromId, fromName);
                return SyncReceiptParts.Held;
            case DayPlanSyncDecision.KeepLocal:
            default:
                break;
        }

        return SyncReceiptParts.Taken;
    }

    private void Ask(SyncDayPlan local, SyncDayPlan incoming, DayPlan incomingPlan, Guid fromId, string fromName)
    {
        if (_pendingClash is { } pending && pending.Incoming.Version == incoming.Version && pending.Local.Version == local.Version)
        {
            return;
        }

        var request = new DayPlanClashRequest(local, incoming, fromName, DayPlanChanges.Describe(_configuration.Current.DayPlan, incomingPlan));
        _pendingClash = request;
        _pendingClashDevice = fromId;
        ClashRaised?.Invoke(this, request);
    }

    private async Task ResolveOnUiThreadAsync(DayPlanClashRequest request, DayPlanClashChoice choice)
    {
        if (!ReferenceEquals(request, _pendingClash))
        {
            return;
        }

        _pendingClash = null;
        SyncDayPlan local = OwnPlan();
        if (choice == DayPlanClashChoice.UseLatest && ReferenceEquals(DayPlanClash.Latest(local, request.Incoming), request.Incoming))
        {
            await TakePlanAsync(request.Incoming);
            return;
        }

        // Kept: it reads as an edit of the other plan, so the other device takes it without asking.
        SetPlanState(local.KeptOver(request.Incoming));
        RaiseOwnDataChanged();
    }

    private async Task TakePlanAsync(SyncDayPlan incoming)
    {
        if (_sessions.IsEnforcing)
        {
            _takeAfterFocus = incoming;
            return;
        }

        if (_configuration.IsReadOnly || ReadPlan(incoming) is not DayPlan plan)
        {
            return;
        }

        // Stamped first, so the change below isn't taken for an edit made here. Nor is anything that follows the plan
        // (sleep's times): taking another device's plan edits no setting of this PC's.
        SetPlanState(incoming.Shared());
        using (_editTimes.SuppressStamping())
        {
            await _configuration.UpdateAsync(document => document with { DayPlan = plan });
        }
    }

    /// <returns>The plan in <paramref name="incoming"/>, or null when it can't be read, isn't the version it says or breaks the plan's rules.</returns>
    private static DayPlan? ReadPlan(SyncDayPlan incoming)
    {
        try
        {
            using JsonDocument json = JsonDocument.Parse(incoming.PlanJson);
            DayPlan plan = ProfileDocumentSerializer.DeserializeDayPlan(json.RootElement);
            return DayPlanVersion.Of(plan) == incoming.Version && DayPlanValidator.Validate(plan) is null ? plan : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private (SessionPhase, DateTimeOffset?, DateTimeOffset?) _lastPhase;

    private void OnSessionStateChanged(SessionState state)
    {
        // A session starting or ending, a new phase or stepping away/back: the file's running session changes (ADR 26).
        var phase = (state.Phase, state.PhaseEndsAt, state.AwaySince);
        if (state.IsRealSession != _sessionActive || (state.IsRealSession && phase != _lastPhase))
        {
            _sessionActive = state.IsRealSession;
            _lastPhase = phase;
            RaiseOwnDataChanged();
        }

        if (!_sessions.IsEnforcing && (_takeAfterFocus is not null || _waitingPlans.Count > 0 || _waitingDocuments.Count > 0))
        {
            _ = ConsiderWaitingAsync();
        }
    }

    private async Task ConsiderWaitingAsync()
    {
        if (_takeAfterFocus is SyncDayPlan chosen)
        {
            _takeAfterFocus = null;
            await TakePlanAsync(chosen);
        }

        List<KeyValuePair<Guid, WaitingPlan>> waiting = _waitingPlans.ToList();
        _waitingPlans.Clear();
        foreach ((Guid fromId, WaitingPlan plan) in waiting)
        {
            string state = await ConsiderPlanAsync(plan.Plan, fromId, plan.FromName, plan.WrittenAt);
            Receipts?.RecordParts(fromId, plan.WrittenAt, new Dictionary<string, string> { ["dayPlan"] = state });
        }

        List<SyncDocument> documents = _waitingDocuments.Values.ToList();
        _waitingDocuments.Clear();
        _ = await ReceiveDocumentsAsync(documents);
    }

    private void OnConfigurationChanged()
    {
        string version = DayPlanVersion.Of(_configuration.Current.DayPlan);
        if (_plan is not null && version != _plan.Version)
        {
            // Edited here (the Day plan tab, Exceptions, a paired phone): it stays based on what this PC last shared.
            SetPlanState(OwnPlan().Edited(ProfileDocumentSerializer.SerializeDayPlan(_configuration.Current.DayPlan).GetRawText(), version, _sessions.Now));
        }

        RaiseOwnDataChanged();
    }

    /// <summary>The plan in effect, with its sync state.</summary>
    private SyncDayPlan OwnPlan()
    {
        DayPlan plan = _configuration.Current.DayPlan;
        PlanState state = _plan ?? new PlanState(DayPlanVersion.Of(plan), DateTimeOffset.UnixEpoch, null);
        return new SyncDayPlan(ProfileDocumentSerializer.SerializeDayPlan(plan).GetRawText(), state.Version, state.EditedAt, state.BasedOn);
    }

    private void SetPlanState(SyncDayPlan plan)
    {
        _plan = new PlanState(plan.Version, plan.EditedAt, plan.BasedOn);
        SavePlanState();
    }

    private void SavePlanState()
    {
        PlanState? toSave = _plan;
        _saving = _saving.ContinueWith(
            async _ =>
            {
                try
                {
                    await _documents.SaveAsync(PlanStateKey, toSave).ConfigureAwait(false);
                }
                catch (SqliteException)
                {
                    // Kept in memory; the next change saves again.
                }
            },
            TaskScheduler.Default).Unwrap();
    }

    /// <summary>
    /// Applies received settings, or keeps them until the focus session ends: like the Profiles tab and Settings,
    /// they are locked then (the newest of each is kept).
    /// </summary>
    private async Task<bool> ReceiveDocumentsAsync(IReadOnlyList<SyncDocument> documents)
    {
        if (!_sessions.IsEnforcing)
        {
            return await ApplyDocumentsAsync(documents);
        }

        foreach (SyncDocument document in documents)
        {
            if (!_waitingDocuments.TryGetValue(document.Key, out SyncDocument? held) || document.EditedAt > held.EditedAt)
            {
                _waitingDocuments[document.Key] = document;
            }
        }

        return true;
    }

    /// <summary>
    /// Tries the newest received goal again, on every Drive poll pass: a lower goal that had to wait is otherwise only
    /// seen again when the sender writes a new file. Not during a focus session (settings are locked then); the next
    /// pass after it tries again. The usual filter skips it when it isn't newer than this PC's own edit.
    /// </summary>
    public async Task RetryGoalAsync()
    {
        if (_sessions.IsEnforcing)
        {
            return;
        }

        SyncDocument? newest = _received.Active
            .SelectMany(device => device.File.Documents)
            .Where(document => document.Key == SyncDocument.GoalKey)
            .MaxBy(document => document.EditedAt);
        if (newest is not null)
        {
            await ApplyDocumentsAsync([newest]);
        }
    }

    /// <summary>Applies each received setting edited later than this PC's own, by this PC's rules.</summary>
    // Returns true when a setting edited later than this PC's own was not adopted yet (a lower goal or a looser change waiting): "held" for the receipt.
    private async Task<bool> ApplyDocumentsAsync(IReadOnlyList<SyncDocument> documents)
    {
        bool adoptedAny = false;
        bool held = false;
        foreach (SyncDocument document in documents)
        {
            if (_editTimes.EditedAt(document.Key) is DateTimeOffset localEdit && document.EditedAt <= localEdit)
            {
                continue;
            }

            try
            {
                DateTimeOffset? editedAt = document.Key switch
                {
                    SyncDocument.ProfilesKey => await ApplyProfilesAsync(document),
                    SyncDocument.SleepKey => await ApplySleepAsync(document.Json) ? document.EditedAt : null,
                    SyncDocument.DayBarKey => await ApplyDayBarAsync(document.Json) ? document.EditedAt : null,
                    SyncDocument.GoalKey => await ApplyGoalAsync(document.Json) ? document.EditedAt : null,
                    _ => null,
                };
                if (editedAt is DateTimeOffset adopted)
                {
                    _editTimes.Adopt(document.Key, adopted);
                    adoptedAny = true;
                }
                else
                {
                    held = true;
                }
            }
            catch (Exception ex) when (ex is JsonException or UnsupportedSchemaVersionException)
            {
                // A setting this version can't read stays as it is here.
            }
        }

        if (adoptedAny)
        {
            // Its file carries the adopted edit times, also when the setting itself was the same here already. The two
            // devices still settle: once one adopted the other's edit time, the file back holds nothing newer for it.
            RaiseOwnDataChanged();
        }

        return held;
    }

    /// <summary>
    /// Through the same guard as an edit here: stricter parts now, looser ones after this PC's delay, never during a
    /// session, and a profile deleted there waits the same way. Profiles this PC never shared (it has no profiles edit
    /// time yet, so they never left it) join the received ones: the other device can't have deleted them.
    /// </summary>
    /// <returns>The edit time this PC's profiles take: the received one, or a later one when its own joined them; null when not applied.</returns>
    private async Task<DateTimeOffset?> ApplyProfilesAsync(SyncDocument document)
    {
        ProfileDocument received = ProfileDocumentSerializer.Deserialize(document.Json);
        if (!IsComplete(received) || _configuration.IsReadOnly)
        {
            return null;
        }

        DateTimeOffset editedAt = document.EditedAt;
        if (_editTimes.EditedAt(SyncDocument.ProfilesKey) is null)
        {
            List<FocusProfile> neverShared = _configuration.Editing.Profiles
                .Where(own => received.Profiles.All(other => other.Id != own.Id))
                .ToList();
            if (neverShared.Count > 0)
            {
                // Both sets together are newer than either, so they travel back to the sender (whose clock may run ahead).
                received = received with { Profiles = [.. received.Profiles, .. neverShared] };
                DateTimeOffset now = _sessions.Now;
                editedAt = now > document.EditedAt ? now : document.EditedAt.AddTicks(1);
            }
        }

        if (WithoutDayPlan(received) != WithoutDayPlan(_configuration.Editing))
        {
            // The plan travels on its own (the clash rule); a received change gets no edit time of this PC's.
            using (_editTimes.SuppressStamping())
            {
                await _configuration.UpdateAsync(current => received with { DayPlan = current.DayPlan }, fromAnotherDevice: true);
            }
        }

        return editedAt;
    }

    // A profiles document missing required parts (a broken or hand-made file) stays out, like one that can't be read:
    // the JSON reader leaves them null, and the change guard would trip over them.
    private static bool IsComplete(ProfileDocument document)
    {
        return document.Profiles is not null && document.PomodoroDefaults is not null && document.Escalation is not null
               && document.Bypass is not null && document.Locks is not null && document.DayPlan is not null
               && document.Profiles.All(profile => profile is not null && profile.Name is not null && profile.Rules is not null);
    }

    /// <summary>On/off, the goal and tonight's one-off wake time; the times follow this PC's day plan.</summary>
    private async Task<bool> ApplySleepAsync(string json)
    {
        if (JsonSerializer.Deserialize<SleepSettings>(json, SyncJson.Options) is not SleepSettings received)
        {
            return false;
        }

        SleepSettings current = _sleep.Current;
        // What sleep shares (ADR 22, 35): on/off, the goal, a one-night wake and the smart bedtime's switches and limits.
        SleepSettings updated = current with
        {
            Enabled = received.Enabled,
            OffWaited = !received.Enabled && received.OffWaited,
            GoalHours = received.GoalHours,
            WakeOnce = received.WakeOnce,
            SmartWorkdays = received.SmartWorkdays,
            SmartOffDays = received.SmartOffDays,
            MinHours = received.MinHours,
            MaxHours = received.MaxHours,
            BedtimeReminders = received.BedtimeReminders ?? current.BedtimeReminders,
        };
        // Turning sleep mode off is a looser change: an off this PC didn't wait for yet (a phone's, or an older version's)
        // waits here too (SaveAsync keeps sleep mode on until then); one the sender waited for applies at once. A received
        // on cancels an off still waiting here.
        if (received.Enabled)
        {
            await _sleep.CancelPendingOffAsync();
        }

        if (updated != current)
        {
            using (_editTimes.SuppressStamping())
            {
                await _sleep.SaveAsync(updated, fromAnotherDevice: true);
            }
        }

        return true;
    }

    private async Task<bool> ApplyDayBarAsync(string json)
    {
        if (JsonSerializer.Deserialize<DayBarSettings>(json, SyncJson.Options) is not DayBarSettings received)
        {
            return false;
        }

        if (JsonSerializer.Serialize(received, SyncJson.Options) != JsonSerializer.Serialize(_dayBar.Current, SyncJson.Options))
        {
            using (_editTimes.SuppressStamping())
            {
                await _dayBar.SaveAsync(received);
            }
        }

        return true;
    }

    /// <summary>
    /// The daily goal: a raise applies at once, a lower one only once <see cref="GoalLowerWaits"/> stops saying wait.
    /// Until then it returns false, so the edit time isn't adopted and the same document is tried on every later sync.
    /// </summary>
    private async Task<bool> ApplyGoalAsync(string json)
    {
        if (JsonSerializer.Deserialize<GoalSettings>(json, SyncJson.Options) is not GoalSettings received || received.DailyMinutes <= 0)
        {
            return false;
        }

        // The same 30-480 range as the goal card.
        int minutes = Math.Clamp(received.DailyMinutes, 30, 480);
        if (minutes == _goal.Current.DailyMinutes)
        {
            return true;
        }

        if (minutes < _goal.Current.DailyMinutes && GoalLowerWaits?.Invoke() == true)
        {
            return false;
        }

        using (_editTimes.SuppressStamping())
        {
            await _goal.SetAsync(minutes);
        }

        return true;
    }

    /// <summary>
    /// The shared settings with an edit time: the profiles as the editors show them (a looser edit still waiting here
    /// travels, and waits its delay on each receiver too), sleep and the day bar. A setting never edited since this PC
    /// started keeping track has no edit time and stays here, so a new device's defaults never replace edited settings.
    /// </summary>
    private List<SyncDocument> SharedDocuments()
    {
        var documents = new List<SyncDocument>();
        Add(SyncDocument.ProfilesKey, WithoutDayPlan(_configuration.Editing));
        Add(SyncDocument.SleepKey, JsonSerializer.Serialize(_sleep.Current, SyncJson.Options));
        Add(SyncDocument.DayBarKey, JsonSerializer.Serialize(_dayBar.Current, SyncJson.Options));
        Add(SyncDocument.GoalKey, JsonSerializer.Serialize(_goal.Current, SyncJson.Options));
        return documents;

        void Add(string key, string json)
        {
            if (_editTimes.EditedAt(key) is DateTimeOffset editedAt)
            {
                documents.Add(new SyncDocument(key, json, editedAt));
            }
        }
    }

    // The newest profile schema a phone before the day plan modes (0.1.61) reads: it refuses a profiles document stamped higher.
    private const int OldPhoneProfileSchemaVersion = 11;

    /// <summary>
    /// The profile document without its plan (the starting plan in its place): the plan travels on its own. Stamped schema
    /// 11, the last one older phones read: v12 and v13 changed only the day plan, which travels in the file's own plan field with
    /// its v12 and v11 fields, so an old reader accepts everything else; new readers upgrade by the plan's shape, not the stamp
    /// (DayModesMigration only rewrites a plan with no templates, DayCycleMigration one with no cycle), and read it back as v13.
    /// </summary>
    private static string WithoutDayPlan(ProfileDocument document)
    {
        return ProfileDocumentSerializer.Serialize(document with { DayPlan = DayPlan.Default, SchemaVersion = OldPhoneProfileSchemaVersion });
    }

    /// <summary>The methods this PC has on, as its card lists them (SYNC1b's board shows one block for each).</summary>
    public IReadOnlyList<string> OwnMethods() => Methods();

    /// <summary>The day plan clash waiting for an answer (changed here and on another device), or null (SYNC1b's conflicts list).</summary>
    public DayPlanClashRequest? PendingClash => _pendingClash;

    private List<string> Methods()
    {
        SyncSettings settings = _settings.Current;
        var methods = new List<string>();
        if (settings.FolderOn)
        {
            methods.Add(SyncDeviceCard.MethodFolder);
        }

        if (settings.QrOn)
        {
            methods.Add(SyncDeviceCard.MethodQr);
        }

        if (settings.FileShareOn)
        {
            methods.Add(SyncDeviceCard.MethodFile);
        }

        if (settings.DriveOn)
        {
            methods.Add(SyncDeviceCard.MethodDrive);
        }

        if (settings.RelayOn)
        {
            methods.Add(SyncDeviceCard.MethodRelay);
        }

        return methods;
    }

    private Dictionary<Guid, DateTimeOffset> Seen()
    {
        return _received.Active.ToDictionary(device => device.File.Device.DeviceId, device => device.File.Device.WrittenAt);
    }

    /// <summary>A new group or none: plans and settings from the old one are no longer this PC's to take.</summary>
    private void ForgetGroupWork()
    {
        // SYNC1b: the lists of unreadable files are the old group's; the record of what arrived and what was sent goes with a group left for good.
        Unreadable?.ClearAll();
        if (_identity.Key is null)
        {
            Receipts?.Forget(new HashSet<Guid>(), all: true);
            SendLog?.Clear();
        }

        _waitingPlans.Clear();
        _waitingDocuments.Clear();
        _takeAfterFocus = null;
        _pendingClash = null;
    }

    private void RaiseOwnDataChanged()
    {
        OwnDataChanged?.Invoke(this, EventArgs.Empty);
    }

    private T? LoadBlocking<T>(string key)
        where T : class
    {
        try
        {
            return Task.Run(() => _documents.LoadAsync<T>(key)).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is SqliteException or JsonException)
        {
            return null;
        }
    }

    /// <param name="BasedOn">See <see cref="SyncDayPlan.BasedOn"/>.</param>
    private sealed record PlanState(string Version, DateTimeOffset EditedAt, string? BasedOn);

    /// <summary>The shape DayPlanEditTimeService saves ("dayplan.editedAt").</summary>
    private sealed record PlanEditTime(string Version, DateTimeOffset? EditedAt);

    private sealed record WaitingPlan(SyncDayPlan Plan, string FromName, DateTimeOffset WrittenAt);
}
