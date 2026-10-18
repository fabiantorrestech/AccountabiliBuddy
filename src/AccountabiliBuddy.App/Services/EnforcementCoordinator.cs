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

    private readonly Dictionary<string, ActivitySnapshot> _latestByMonitor = new(StringComparer.Ordinal);
    private readonly ScreenTextCache _screenText = new();
    private bool _isReadingScreen;

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
        WatchedBrowsersService watchedBrowsers)
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

        _watcher.ActivityChanged += OnActivityChanged;
        _watcher.MonitorDisconnected += OnMonitorDisconnected;
        _browsers.ConnectionsChanged += OnBrowserConnectionsChanged;
        _sessions.StateChanged += OnSessionStateChanged;
        _sessions.Ticked += OnSessionTicked;
        _configuration.Changed += OnConfigurationChanged;
        _privacy.Changed += OnPrivacyChanged;

        _presenter.RecoveryRequested += OnRecoveryRequested;
        _presenter.RuleCreated += OnRuleCreated;
        _presenter.ClassificationDeferred += OnClassificationDeferred;
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

    private void OnClassificationDeferred(object? sender, string subject)
    {
        _engine.DeferClassification(subject);
        Reevaluate();
    }

    private async void OnRuleCreated(object? sender, ClassifyAnswer answer)
    {
        if (_sessions.State.ProfileId is not Guid profileId)
        {
            return;
        }

        _recorder.RecordClassification(answer.Rule, answer.Snapshot, profileId);

        // Saving notifies Changed, which re-evaluates with the new rule.
        await _configuration.AddRulesAsync(profileId, new[] { answer.Rule });
    }

    private void Reevaluate()
    {
        SessionState state = _sessions.State;
        IReadOnlyList<FocusProfile> profiles = _configuration.Current.Profiles;
        FocusProfile? profile = profiles.FirstOrDefault(candidate => candidate.Id == state.ProfileId);
        DateTimeOffset now = _sessions.Now;

        IReadOnlyList<BrowserConnection> browsers = _browsers.Connections;
        bool readScreens = ShouldReadScreens(state, profile);
        List<ActivitySnapshot> snapshots = _latestByMonitor.Values
            .Select(snapshot => BrowserTabMatcher.Enrich(snapshot, browsers, _watchedBrowsers.Watched))
            .Select(snapshot => readScreens ? WithScreenText(snapshot) : snapshot)
            .ToList();

        IReadOnlyList<MonitorEnforcement> decisions = _engine.Evaluate(
            state,
            profile,
            _configuration.Current.Escalation,
            snapshots,
            now);

        string profileName = ProfileOptions.NameOf(profiles, state.ProfileId) ?? "this session";
        _presenter.Apply(decisions, profileName, SessionStatusFormatter.Detail(state, now));
        _recorder.Observe(decisions, state, now);

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
