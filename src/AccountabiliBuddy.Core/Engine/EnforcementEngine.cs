using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Decides, for every monitor, how strongly to respond to what is on screen during a focus phase.
/// Builds on <see cref="DriftTracker"/> (nudge → overlay → enforce over time) and adds:
/// <list type="bullet">
/// <item>Scope: a whole-app block is minimized at the enforce level; a view block (title/URL) keeps the
/// overlay instead, so the user is never locked out of an app that is partly allowed.</item>
/// <item>Repeat offenses: once a whole-app block was minimized, reopening it in the same focus phase is
/// minimized immediately. Once a view block reached the overlay, returning to it brings the overlay back immediately.</item>
/// <item>Recovery: the user can ask for <see cref="RecoveryWindow"/> to navigate away from a blocked view.
/// Nothing escalates past a nudge on that monitor until it runs out.</item>
/// </list>
/// All state resets when a new focus phase begins or enforcement stops.
/// Not thread-safe: call from a single thread.
/// </summary>
public sealed class EnforcementEngine
{
    public static TimeSpan RecoveryWindow { get; } = TimeSpan.FromSeconds(15);

    private readonly RuleEvaluator _evaluator;

    private readonly HashSet<Guid> _minimizedAppRuleIds = new();
    private readonly HashSet<Guid> _overlaidViewRuleIds = new();
    private readonly Dictionary<string, DateTimeOffset> _recoveryEndsByMonitor = new(StringComparer.Ordinal);
    private readonly HashSet<string> _deferredProcessNames = new(StringComparer.OrdinalIgnoreCase);

    private DriftTracker? _tracker;
    private PhaseKey? _currentPhase;

    public EnforcementEngine(RuleEvaluator evaluator)
    {
        _evaluator = evaluator;
    }

    /// <param name="profile">The session's profile, or null if it no longer exists.</param>
    /// <param name="snapshots">The latest snapshot of every connected monitor.</param>
    public IReadOnlyList<MonitorEnforcement> Evaluate(
        SessionState state,
        FocusProfile? profile,
        EscalationSettings settings,
        IEnumerable<ActivitySnapshot> snapshots,
        DateTimeOffset now)
    {
        if (!state.IsEnforcing || profile is null)
        {
            EndPhase();
            return snapshots
                .Select(snapshot => MonitorEnforcement.Relaxed(snapshot, Classification.OnTrack))
                .ToList();
        }

        DriftTracker tracker = BeginPhaseIfNew(state, settings);

        // Snapshots only arrive when something changes; re-stamping them lets drift keep growing in between.
        return snapshots
            .Select(snapshot => EvaluateMonitor(tracker, profile, snapshot with { Timestamp = now }, now))
            .ToList();
    }

    /// <summary>Starts the "Navigate away" window on a monitor showing a blocked view.</summary>
    public void StartRecovery(string monitorId, DateTimeOffset now)
    {
        if (_currentPhase is not null)
        {
            _recoveryEndsByMonitor[monitorId] = now + RecoveryWindow;
        }
    }

    /// <summary>"Not now": stop asking about this app until the next focus phase.</summary>
    public void DeferClassification(string processName)
    {
        if (_currentPhase is not null)
        {
            _deferredProcessNames.Add(processName);
        }
    }

    /// <summary>Drops state for a monitor that was disconnected.</summary>
    public void ForgetMonitor(string monitorId)
    {
        _tracker?.Forget(monitorId);
        _recoveryEndsByMonitor.Remove(monitorId);
    }

    private MonitorEnforcement EvaluateMonitor(
        DriftTracker tracker, FocusProfile profile, ActivitySnapshot snapshot, DateTimeOffset now)
    {
        // An empty desktop counts as back on track, which also resets any drift on that monitor.
        ClassificationResult result = snapshot.IsEmptyDesktop
            ? new ClassificationResult(Classification.OnTrack, null)
            : _evaluator.Evaluate(profile, snapshot);

        DriftAssessment drift = tracker.Observe(snapshot, result, profile.UnknownGracePeriod);

        bool needsClassification = drift.NeedsClassification
            && !_deferredProcessNames.Contains(snapshot.ProcessName);

        if (result.Classification != Classification.Distracting || result.MatchedRule is null)
        {
            return MonitorEnforcement.Relaxed(snapshot, result.Classification) with
            {
                NeedsClassification = needsClassification,
            };
        }

        Rule rule = result.MatchedRule;
        BlockScope scope = rule.Target == MatchTarget.ProcessName ? BlockScope.App : BlockScope.View;

        EnforcementAction action = ToAction(drift.Level, scope);

        // An earlier escalation of the same rule sets a floor; it only counts as a repeat when it raises the response.
        EnforcementAction floor = EnforcementAction.None;
        if (scope == BlockScope.App && _minimizedAppRuleIds.Contains(rule.Id))
        {
            floor = EnforcementAction.Minimize;
        }
        else if (scope == BlockScope.View && _overlaidViewRuleIds.Contains(rule.Id))
        {
            floor = EnforcementAction.Overlay;
        }

        bool isRepeatOffense = floor > action;
        if (isRepeatOffense)
        {
            action = floor;
        }

        bool isRecovering = IsRecovering(snapshot.MonitorId, now);
        if (isRecovering && scope == BlockScope.View && action > EnforcementAction.Nudge)
        {
            action = EnforcementAction.Nudge;
        }

        RememberEscalation(rule.Id, scope, action);

        return new MonitorEnforcement(
            snapshot,
            Classification.Distracting,
            action,
            scope,
            rule,
            isRepeatOffense,
            isRecovering,
            NeedsClassification: false);
    }

    private static EnforcementAction ToAction(EscalationLevel level, BlockScope scope)
    {
        return level switch
        {
            EscalationLevel.Nudge => EnforcementAction.Nudge,
            EscalationLevel.Overlay => EnforcementAction.Overlay,
            // A view block never minimizes the whole app; the overlay stays until the user navigates away.
            EscalationLevel.Enforce => scope == BlockScope.App ? EnforcementAction.Minimize : EnforcementAction.Overlay,
            _ => EnforcementAction.None,
        };
    }

    private void RememberEscalation(Guid ruleId, BlockScope scope, EnforcementAction action)
    {
        if (scope == BlockScope.App && action == EnforcementAction.Minimize)
        {
            _minimizedAppRuleIds.Add(ruleId);
        }
        else if (scope == BlockScope.View && action == EnforcementAction.Overlay)
        {
            _overlaidViewRuleIds.Add(ruleId);
        }
    }

    private bool IsRecovering(string monitorId, DateTimeOffset now)
    {
        if (!_recoveryEndsByMonitor.TryGetValue(monitorId, out DateTimeOffset endsAt))
        {
            return false;
        }

        if (now < endsAt)
        {
            return true;
        }

        _recoveryEndsByMonitor.Remove(monitorId);
        return false;
    }

    private DriftTracker BeginPhaseIfNew(SessionState state, EscalationSettings settings)
    {
        var phase = new PhaseKey(state.Mode, state.PhaseStartedAt);
        if (_tracker is not null && phase == _currentPhase)
        {
            return _tracker;
        }

        ClearPhaseState();
        _currentPhase = phase;
        _tracker = new DriftTracker(new EscalationPolicy(settings));
        return _tracker;
    }

    private void EndPhase()
    {
        if (_currentPhase is not null)
        {
            ClearPhaseState();
        }
    }

    private void ClearPhaseState()
    {
        _currentPhase = null;
        _tracker = null;
        _minimizedAppRuleIds.Clear();
        _overlaidViewRuleIds.Clear();
        _recoveryEndsByMonitor.Clear();
        _deferredProcessNames.Clear();
    }

    /// <summary>Identifies one focus phase; a new value means a new phase started.</summary>
    private sealed record PhaseKey(SessionMode? Mode, DateTimeOffset? StartedAt);
}
