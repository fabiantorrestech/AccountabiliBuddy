using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Connects the activity watcher, the session and the <see cref="EnforcementEngine"/> to the
/// <see cref="EnforcementPresenter"/>. Re-evaluates every monitor when activity changes, when browser tabs
/// change, when the session changes, and once a second so drift keeps escalating while nothing on screen changes.
/// Browser windows get their active tab's URL, tab id and channel from the extension before evaluation.
/// Screen reading (M6) is the last, rare step of the detection cascade: only when granted in Setup, during focus,
/// for a profile with screen-text rules, and only for windows no other rule recognised; one read at a time, each
/// view at most once a minute (<see cref="ScreenTextCache"/>). Every evaluation is also handed to the
/// <see cref="ActivityRecorder"/> (M7).
/// All work happens on the UI thread.
/// </summary>
public sealed class EnforcementCoordinator : IDisposable
{
    private readonly IActivityWatcher _watcher;
    private readonly IBrowserActivitySource _browsers;
    private readonly SessionController _sessions;
    private readonly ConfigurationService _configuration;
    private readonly EnforcementEngine _engine;
    private readonly EnforcementPresenter _presenter;
    private readonly IScreenTextReader _screenReader;
    private readonly PrivacySettingsService _privacy;
    private readonly ActivityRecorder _recorder;
    private readonly WatchedBrowsersService _watchedBrowsers;
    private readonly PrivateWindowAccessMonitor _privateAccess;

    private readonly Dictionary<string, ActivitySnapshot> _latestByMonitor = new(StringComparer.Ordinal);

    private PlanFirstService? _planFirst;

    /// <summary>Plan first: its hold on unknowns, and where unknowns seen while planning are noted. Set by the app.</summary>
    public PlanFirstService? PlanFirst
    {
        get => _planFirst;
        set
        {
            _planFirst = value;
            if (value is not null)
            {
                // Planning over: unknowns can be asked about now.
                value.Ended += (_, _) => Reevaluate();
            }
        }
    }

    // What was decided last; kept for the recorder while the user has stepped away (nothing is decided then).
    private IReadOnlyList<MonitorEnforcement> _lastDecisions = Array.Empty<MonitorEnforcement>();
    private readonly ScreenTextCache _screenText = new();
    private bool _isReadingScreen;

    // "On track (this session)" answers: allowed until the session ends, never saved.
    private readonly List<Rule> _sessionRules = new();
    private Guid? _sessionRulesProfileId;

    public EnforcementCoordinator(
        IActivityWatcher watcher,
        IBrowserActivitySource browsers,
        SessionController sessions,
        ConfigurationService configuration,
        EnforcementEngine engine,
        EnforcementPresenter presenter,
        IScreenTextReader screenReader,
        PrivacySettingsService privacy,
        ActivityRecorder recorder,
        WatchedBrowsersService watchedBrowsers,
        PrivateWindowAccessMonitor privateAccess)
    {
        _watcher = watcher;
        _browsers = browsers;
        _sessions = sessions;
        _configuration = configuration;
        _engine = engine;
        _presenter = presenter;
        _screenReader = screenReader;
        _privacy = privacy;
        _recorder = recorder;
        _watchedBrowsers = watchedBrowsers;
        _privateAccess = privateAccess;

        _watcher.ActivityChanged += OnActivityChanged;
        _watcher.MonitorDisconnected += OnMonitorDisconnected;
        _browsers.ConnectionsChanged += OnBrowserConnectionsChanged;
        _sessions.StateChanged += OnSessionStateChanged;
        _sessions.Ticked += OnSessionTicked;
        _configuration.Changed += OnConfigurationChanged;
        _privacy.Changed += OnPrivacyChanged;

        _presenter.RecoveryRequested += OnRecoveryRequested;
        _presenter.Answered += OnClassifyAnswered;
    }

    public void Dispose()
    {
        _watcher.ActivityChanged -= OnActivityChanged;
        _watcher.MonitorDisconnected -= OnMonitorDisconnected;
        _browsers.ConnectionsChanged -= OnBrowserConnectionsChanged;
        _sessions.StateChanged -= OnSessionStateChanged;
        _sessions.Ticked -= OnSessionTicked;
        _configuration.Changed -= OnConfigurationChanged;
        _privacy.Changed -= OnPrivacyChanged;

        _presenter.CloseAll();
    }

    // Watcher events arrive on the watcher thread.
    private void OnActivityChanged(object? sender, ActivitySnapshot snapshot)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _latestByMonitor[snapshot.MonitorId] = snapshot;
            Reevaluate();
        });
    }

    private void OnMonitorDisconnected(object? sender, string monitorId)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _latestByMonitor.Remove(monitorId);
            _engine.ForgetMonitor(monitorId);
            _presenter.ForgetMonitor(monitorId);
        });
    }

    // Browser events arrive on pipe threads.
    private void OnBrowserConnectionsChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(Reevaluate);
    }

    private void OnSessionStateChanged(object? sender, SessionState state)
    {
        if (!state.IsActive || state.ProfileId != _sessionRulesProfileId)
        {
            _sessionRules.Clear();
            _sessionRulesProfileId = null;
        }

        Reevaluate();
    }

    private void OnSessionTicked(object? sender, EventArgs e)
    {
        Reevaluate();
    }

    private void OnConfigurationChanged(object? sender, ProfileDocument document)
    {
        Reevaluate();
    }

    private void OnPrivacyChanged(object? sender, PrivacySettings settings)
    {
        if (!settings.ScreenReadingEnabled)
        {
            // Revoked: forget everything that was read.
            _screenText.Clear();
        }

        Reevaluate();
    }

    private void OnRecoveryRequested(object? sender, string monitorId)
    {
        _engine.StartRecovery(monitorId, _sessions.Now);
        Reevaluate();
    }

    private async void OnClassifyAnswered(object? sender, ClassifyAnswer answer)
    {
        // The profile it was asked for (the stacked prompt names it), else the session's.
        if ((answer.ProfileId ?? _sessions.State.ProfileId) is not Guid profileId)
        {
            return;
        }

        _recorder.RecordClassification(answer, profileId);

        if (answer.Lifetime == LabelLifetime.Session)
        {
            if (_sessions.State.ProfileId != profileId)
            {
                return; // That session is over: "on track for this session" has nothing left to apply to.
            }

            _sessionRules.Add(answer.Rule);
            _sessionRulesProfileId = profileId;
            Reevaluate();
        }
        else if (answer.Rule.Verdict == RuleVerdict.Block)
        {
            // Stricter, so it takes effect at once; saving notifies Changed, which re-evaluates.
            await _configuration.AddRulesAsync(profileId, new[] { answer.Rule });
        }
        else
        {
            // "Add exemption": the typed reason is the friction, so it skips the wait for looser changes.
            await _configuration.AddRuleNowAsync(answer.OtherProfileIds.Prepend(profileId).ToList(), answer.Rule);
        }
    }

    private void Reevaluate()
    {
        SessionState state = _sessions.State;
        if (state.PausesEnforcement)
        {
            // Stepped away: everything stays as it was (what was blocked stays blocked, an open prompt stays open, the
            // escalation doesn't move or start over) until the user is back. Only the recorder hears the idle time.
            // A side task is not this: the focus clock pauses, enforcement keeps running.
            _recorder.Observe(_lastDecisions, state, _sessions.Now);
            return;
        }

        IReadOnlyList<FocusProfile> profiles = _configuration.Current.Profiles;
        FocusProfile? profile = profiles.FirstOrDefault(candidate => candidate.Id == state.ProfileId);
        if (profile is not null && _sessionRules.Count > 0)
        {
            profile = profile with { Rules = profile.Rules.Concat(_sessionRules).ToList() };
        }

        DateTimeOffset now = _sessions.Now;

        IReadOnlyList<BrowserConnection> browsers = _browsers.Connections;
        bool readScreens = ShouldReadScreens(state, profile);
        List<ActivitySnapshot> snapshots = _latestByMonitor.Values
            .Select(snapshot => BrowserTabMatcher.Enrich(snapshot, browsers, _watchedBrowsers.Watched, _privateAccess.Missing))
            .Select(snapshot => readScreens ? WithScreenText(snapshot) : snapshot)
            .ToList();

        _engine.HoldUnknownsUntil = _planFirst?.HoldUntil;
        // Comms windows (schema v6): on a day with Comms parts, mail and chat only inside them.
        _engine.InCommsWindow = CommsWindows.At(
            _configuration.Current.DayPlan, TimeZoneInfo.ConvertTime(now, _sessions.TimeProvider.LocalTimeZone).DateTime);
        IReadOnlyList<MonitorEnforcement> decisions = _engine.Evaluate(
            state,
            profile,
            _configuration.Current.Escalation,
            snapshots,
            now);

        string profileName = ProfileOptions.NameOf(profiles, state.ProfileId) ?? "this session";
        IReadOnlyList<ProfileOption> otherProfiles = profiles
            .Where(candidate => candidate.Id != state.ProfileId)
            .Select(ProfileOption.From)
            .ToList();
        _presenter.Apply(decisions, profileName, SessionStatusFormatter.Detail(state, now), otherProfiles);
        _recorder.Observe(decisions, state, now);
        _lastDecisions = decisions;
        if (_planFirst is { IsPlanning: true } planning && state.IsEnforcing)
        {
            foreach (MonitorEnforcement decision in decisions.Where(decision => decision.Classification == Classification.Unknown))
            {
                planning.NoteUnknown(decision.Snapshot);
            }
        }

        if (readScreens)
        {
            ReadNextUnknownScreen(decisions, now);
        }
    }

    private bool ShouldReadScreens(SessionState state, FocusProfile? profile)
    {
        return _privacy.Current.ScreenReadingEnabled && state.IsEnforcing && profile?.HasOcrRules == true;
    }

    private ActivitySnapshot WithScreenText(ActivitySnapshot snapshot)
    {
        return _screenText.GetText(snapshot) is { } text ? snapshot with { OcrText = text } : snapshot;
    }

    /// <summary>Starts one screen read for a window nothing else recognised, if one is due and none is running.</summary>
    private void ReadNextUnknownScreen(IReadOnlyList<MonitorEnforcement> decisions, DateTimeOffset now)
    {
        if (_isReadingScreen)
        {
            return;
        }

        MonitorEnforcement? unknown = decisions.FirstOrDefault(decision =>
            decision.Classification == Classification.Unknown && _screenText.IsDue(decision.Snapshot, now));
        if (unknown is null)
        {
            return;
        }

        _isReadingScreen = true;
        _screenText.MarkPending(unknown.Snapshot);
        _ = ReadScreenAsync(unknown.Snapshot);
    }

    private async Task ReadScreenAsync(ActivitySnapshot snapshot)
    {
        string? text = null;
        try
        {
            // Capture and OCR take a few hundred milliseconds; keep them off the UI thread.
            text = await Task.Run(() => _screenReader.ReadTextAsync(snapshot));
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException
                                       or ArgumentException or OperationCanceledException)
        {
            // Unreadable window (closed meanwhile, protected content): treated as "nothing read".
        }
        finally
        {
            _screenText.Store(snapshot, text, _sessions.Now);
            _isReadingScreen = false;
        }

        // The new text may now match a screen-text rule.
        Reevaluate();
    }
}
