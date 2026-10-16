using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Connects the activity watcher, the session and the <see cref="EnforcementEngine"/> to the
/// <see cref="EnforcementPresenter"/>. Re-evaluates every monitor when activity changes, when the
/// session changes, and once a second so drift keeps escalating while nothing on screen changes.
/// All work happens on the UI thread.
/// </summary>
public sealed class EnforcementCoordinator : IDisposable
{
    private readonly IActivityWatcher _watcher;
    private readonly SessionController _sessions;
    private readonly ConfigurationService _configuration;
    private readonly EnforcementEngine _engine;
    private readonly EnforcementPresenter _presenter;

    private readonly Dictionary<string, ActivitySnapshot> _latestByMonitor = new(StringComparer.Ordinal);

    public EnforcementCoordinator(
        IActivityWatcher watcher,
        SessionController sessions,
        ConfigurationService configuration,
        EnforcementEngine engine,
        EnforcementPresenter presenter)
    {
        _watcher = watcher;
        _sessions = sessions;
        _configuration = configuration;
        _engine = engine;
        _presenter = presenter;

        _watcher.ActivityChanged += OnActivityChanged;
        _watcher.MonitorDisconnected += OnMonitorDisconnected;
        _sessions.StateChanged += OnSessionStateChanged;
        _sessions.Ticked += OnSessionTicked;
        _configuration.Changed += OnConfigurationChanged;

        _presenter.RecoveryRequested += OnRecoveryRequested;
        _presenter.RuleCreated += OnRuleCreated;
        _presenter.ClassificationDeferred += OnClassificationDeferred;
    }

    public void Dispose()
    {
        _watcher.ActivityChanged -= OnActivityChanged;
        _watcher.MonitorDisconnected -= OnMonitorDisconnected;
        _sessions.StateChanged -= OnSessionStateChanged;
        _sessions.Ticked -= OnSessionTicked;
        _configuration.Changed -= OnConfigurationChanged;

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

    private void OnRecoveryRequested(object? sender, string monitorId)
    {
        _engine.StartRecovery(monitorId, _sessions.Now);
        Reevaluate();
    }

    private void OnClassificationDeferred(object? sender, string processName)
    {
        _engine.DeferClassification(processName);
        Reevaluate();
    }

    private async void OnRuleCreated(object? sender, Rule rule)
    {
        if (_sessions.State.ProfileId is not Guid profileId)
        {
            return;
        }

        // Saving notifies Changed, which re-evaluates with the new rule.
        await _configuration.UpdateAsync(document => document with
        {
            Profiles = document.Profiles
                .Select(profile => profile.Id == profileId
                    ? profile with { Rules = profile.Rules.Append(rule).ToList() }
                    : profile)
                .ToList(),
        });
    }

    private void Reevaluate()
    {
        SessionState state = _sessions.State;
        IReadOnlyList<FocusProfile> profiles = _configuration.Current.Profiles;
        FocusProfile? profile = profiles.FirstOrDefault(candidate => candidate.Id == state.ProfileId);
        DateTimeOffset now = _sessions.Now;

        IReadOnlyList<MonitorEnforcement> decisions = _engine.Evaluate(
            state,
            profile,
            _configuration.Current.Escalation,
            _latestByMonitor.Values,
            now);

        string profileName = ProfileOptions.NameOf(profiles, state.ProfileId) ?? "this session";
        _presenter.Apply(decisions, profileName, SessionStatusFormatter.Detail(state, now));
    }
}
