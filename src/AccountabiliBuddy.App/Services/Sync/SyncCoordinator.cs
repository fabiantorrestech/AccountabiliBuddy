using System.Text.Json;
using AccountabiliBuddy.App.Services.Backup;
using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Link;
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
        _sessionActive = sessions.State.IsActive;

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
        return Dispatcher.UIThread.InvokeAsync(BuildOwnFileOnUiThreadAsync);
    }

    /// <summary>Takes in a file another device sealed, carried by <paramref name="method"/> (see <see cref="SyncDeviceCard"/>).</summary>
    /// <returns>Null when accepted (or already held); otherwise why not, e.g. <see cref="CannotReadReason"/>.</returns>
    public Task<string?> AcceptSealedAsync(byte[] sealedBytes, string method)
    {
        return Dispatcher.UIThread.InvokeAsync(() => AcceptOnUiThreadAsync(sealedBytes, method));
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
        TimeZoneInfo zone = _sessions.TimeProvider.LocalTimeZone;
        DateTimeOffset now = _sessions.Now;
        bool historyOff = !_privacy.Current.ActivityLogEnabled;
        var card = new SyncDeviceCard(
            _identity.DeviceId, _identity.DeviceName, SyncDeviceCard.KindPc, BackupService.AppVersion, now, Methods(), historyOff, Seen());

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
        return SyncFileBuilder.Build(card, records, drift, _omitted.IsOmitted, id => ProfileOptions.NameOf(profiles, id),
            _sessions.State.IsActive, OwnPlan(), SharedDocuments(), now, zone, oldestSessionStart, TasksOf) with { Comebacks = Comebacks?.Invoke(), Lunch = Lunch?.Invoke(), Acks = Acks?.Invoke() is { Count: > 0 } acks ? acks : null, Running = RunningNow(profiles), Events = Events?.Invoke(),
                Pickups = PcPickups?.PickupsForOtherPcs(), InputRuns = PcPickups?.InputRunsToday(),
                ReminderActions = ReminderActions?.Invoke() is { Count: > 0 } actions ? actions : null,
                Blockers = Blockers?.Invoke(), Beeminder = Beeminder?.Invoke(),
                ActionResults = ActionResults?.Invoke() is { Count: > 0 } results ? results : null,
                EndEarly = EndEarlyOffer?.Invoke(), GoalMinutes = GoalMinutes?.Invoke(),
                // ADR 42: always, read-only, for the phone's start picker (the profiles document travels only once edited).
                ProfileOptions = profiles.Select(profile => new SyncProfileOption(profile.Id, profile.Name)).ToList(),
                DefaultProfileId = CountUpProfileId?.Invoke(), Noise = noise };
    }

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

    /// <summary>The PC's open come-backs for its sync file (ADR 30); set by the app.</summary>
    public Func<IReadOnlyList<SyncComeback>?>? Comebacks { get; set; }

    // The running session for the phone's countdown without the link (ADR 26); old scheduled blocks don't count.
    private SyncRunning? RunningNow(IReadOnlyList<FocusProfile> profiles)
    {
        SessionState state = _sessions.State;
        if (!state.IsActive || state.Mode == SessionMode.Scheduled || state.PhaseStartedAt is not DateTimeOffset started)
        {
            return null;
        }

        return new SyncRunning(ProfileOptions.NameOf(profiles, state.ProfileId), started, state.PhaseEndsAt, state.Phase is SessionPhase.ShortBreak or SessionPhase.LongBreak, state.AwaySince,
            CurrentTask?.Invoke(), state.ProfileId, CountUpProfileId?.Invoke());
    }

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

    /// <summary>The visible calendars' events for the other devices (ADR 26, the link's shape); set by the app.</summary>
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

    private async Task<string?> AcceptOnUiThreadAsync(byte[] sealedBytes, string method)
    {
        if (_identity.Key is not SyncKey key)
        {
            return NotInGroupReason;
        }

        if (SyncFileCodec.Open(sealedBytes, key) is not SyncFile file)
        {
            return CannotReadReason;
        }

        SyncDeviceCard sender = file.Device;
        if (sender.DeviceId == _identity.DeviceId)
        {
            return OwnFileReason;
        }

        ReceivedDevice? stored = _received.Devices.FirstOrDefault(device => device.File.Device.DeviceId == sender.DeviceId);
        if (stored is not null && sender.WrittenAt < stored.File.Device.WrittenAt)
        {
            return $"This PC already has newer data from {sender.Name}.";
        }

        await _received.AcceptAsync(file, method, sealedBytes);
        if (file.DayPlan is SyncDayPlan plan)
        {
            await ConsiderPlanAsync(plan, sender.DeviceId, sender.Name);
        }

        await ReceiveDocumentsAsync(file.Documents);
        return null;
    }

    private async Task ConsiderPlanAsync(SyncDayPlan incoming, Guid fromId, string fromName)
    {
        // The plan is locked during focus: it waits until the session ends.
        if (_sessions.IsEnforcing)
        {
            _waitingPlans[fromId] = new WaitingPlan(incoming, fromName);
            return;
        }

        if (ReadPlan(incoming) is not DayPlan incomingPlan)
        {
            return;
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
                break;
            case DayPlanSyncDecision.Ask:
                Ask(local, incoming, incomingPlan, fromId, fromName);
                break;
            case DayPlanSyncDecision.KeepLocal:
            default:
                break;
        }
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
        if (state.IsActive != _sessionActive || phase != _lastPhase)
        {
            _sessionActive = state.IsActive;
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
            await ConsiderPlanAsync(plan.Plan, fromId, plan.FromName);
        }

        List<SyncDocument> documents = _waitingDocuments.Values.ToList();
        _waitingDocuments.Clear();
        await ReceiveDocumentsAsync(documents);
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
    private async Task ReceiveDocumentsAsync(IReadOnlyList<SyncDocument> documents)
    {
        if (!_sessions.IsEnforcing)
        {
            await ApplyDocumentsAsync(documents);
            return;
        }

        foreach (SyncDocument document in documents)
        {
            if (!_waitingDocuments.TryGetValue(document.Key, out SyncDocument? held) || document.EditedAt > held.EditedAt)
            {
                _waitingDocuments[document.Key] = document;
            }
        }
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

        SyncDocument? newest = _received.Devices
            .SelectMany(device => device.File.Documents)
            .Where(document => document.Key == SyncDocument.GoalKey)
            .MaxBy(document => document.EditedAt);
        if (newest is not null)
        {
            await ApplyDocumentsAsync([newest]);
        }
    }

    /// <summary>Applies each received setting edited later than this PC's own, by this PC's rules.</summary>
    private async Task ApplyDocumentsAsync(IReadOnlyList<SyncDocument> documents)
    {
        bool adoptedAny = false;
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
        if (JsonSerializer.Deserialize<SleepSettings>(json, LinkFraming.JsonOptions) is not SleepSettings received)
        {
            return false;
        }

        SleepSettings current = _sleep.Current;
        // What sleep shares (ADR 22, 35): on/off, the goal, a one-night wake and the smart bedtime's switches and limits.
        SleepSettings updated = current with
        {
            Enabled = received.Enabled,
            GoalHours = received.GoalHours,
            WakeOnce = received.WakeOnce,
            SmartWorkdays = received.SmartWorkdays,
            SmartOffDays = received.SmartOffDays,
            MinHours = received.MinHours,
            MaxHours = received.MaxHours,
            BedtimeReminders = received.BedtimeReminders ?? current.BedtimeReminders,
        };
        if (updated != current)
        {
            using (_editTimes.SuppressStamping())
            {
                await _sleep.SaveAsync(updated);
            }
        }

        return true;
    }

    private async Task<bool> ApplyDayBarAsync(string json)
    {
        if (JsonSerializer.Deserialize<DayBarSettings>(json, LinkFraming.JsonOptions) is not DayBarSettings received)
        {
            return false;
        }

        if (JsonSerializer.Serialize(received, LinkFraming.JsonOptions) != JsonSerializer.Serialize(_dayBar.Current, LinkFraming.JsonOptions))
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
        if (JsonSerializer.Deserialize<GoalSettings>(json, LinkFraming.JsonOptions) is not GoalSettings received || received.DailyMinutes <= 0)
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
        Add(SyncDocument.SleepKey, JsonSerializer.Serialize(_sleep.Current, LinkFraming.JsonOptions));
        Add(SyncDocument.DayBarKey, JsonSerializer.Serialize(_dayBar.Current, LinkFraming.JsonOptions));
        Add(SyncDocument.GoalKey, JsonSerializer.Serialize(_goal.Current, LinkFraming.JsonOptions));
        return documents;

        void Add(string key, string json)
        {
            if (_editTimes.EditedAt(key) is DateTimeOffset editedAt)
            {
                documents.Add(new SyncDocument(key, json, editedAt));
            }
        }
    }

    /// <summary>The profile document without its plan (the starting plan in its place): the plan travels on its own.</summary>
    private static string WithoutDayPlan(ProfileDocument document)
    {
        return ProfileDocumentSerializer.Serialize(document with { DayPlan = DayPlan.Default });
    }

    private List<string> Methods()
    {
        SyncSettings settings = _settings.Current;
        var methods = new List<string>();
        if (settings.LinkOn)
        {
            methods.Add(SyncDeviceCard.MethodLink);
        }

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

        return methods;
    }

    private Dictionary<Guid, DateTimeOffset> Seen()
    {
        return _received.Devices.ToDictionary(device => device.File.Device.DeviceId, device => device.File.Device.WrittenAt);
    }

    /// <summary>A new group or none: plans and settings from the old one are no longer this PC's to take.</summary>
    private void ForgetGroupWork()
    {
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

    private sealed record WaitingPlan(SyncDayPlan Plan, string FromName);
}
