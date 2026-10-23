using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Decides, for every monitor, how strongly to respond to what is on screen during a focus phase.
/// Builds on <see cref="DriftTracker"/> (nudge → overlay → enforce over time) and adds:
/// <list type="bullet">
/// <item>Scope: a whole-app block is minimized at the enforce level; a view block (title/URL/channel) keeps the
/// overlay instead, so the user is never locked out of an app that is partly allowed. When the view is a browser
/// tab the extension reported, the tab is sent to the block page instead.</item>
/// <item>Browser extension pages (our block page, Cold Turkey's) always count as on track.</item>
/// <item>Repeat offenses: once a whole-app block was minimized, reopening it in the same focus phase is
/// minimized immediately. Once a view block reached the overlay, returning to it brings the overlay back immediately.</item>
/// <item>Recovery: the user can ask for <see cref="RecoveryWindow"/> to navigate away from a blocked view.
/// Nothing escalates past a nudge on that monitor until it runs out.</item>
/// <item>Unknown activity that outlasts the grace period escalates as a view block up to the overlay
/// (<see cref="UnknownActivity"/>) while the classify prompt asks about it; there is no "not now".</item>
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

    private DriftTracker? _tracker;
    private PhaseKey? _currentPhase;

    public EnforcementEngine(RuleEvaluator evaluator)
    {
        _evaluator = evaluator;
    }

    /// <summary>
    /// The furthest escalation goes (the Work computer type: <see cref="EscalationLevel.Overlay"/>, never minimizing or
    /// redirecting a work window). Caps every decision, a repeat offense's floor included.
    /// </summary>
    public EscalationLevel MaxLevel { get; set; } = EscalationLevel.Enforce;

    /// <summary>
    /// Plan first (a session's first minutes): until then unknown activity isn't asked about or escalated, and its grace
    /// period starts then. Rules still enforce. Null: no hold.
    /// </summary>
    public DateTimeOffset? HoldUnknownsUntil { get; set; }

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

    /// <summary>What a classify question is about: the site for a browser tab, otherwise the app.</summary>
    public static string ClassificationSubject(ActivitySnapshot snapshot)
    {
        return (snapshot.Url is null ? null : UrlNormalizer.GetHost(snapshot.Url)) ?? snapshot.ProcessName;
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
        // An empty desktop or a blocker's own page counts as back on track, which also resets drift on that monitor.
        ClassificationResult result = snapshot.IsEmptyDesktop || snapshot.IsBrowserExtensionPage
            ? new ClassificationResult(Classification.OnTrack, null)
            : BrowserWithoutPrivateAccess.Apply(snapshot, UnseenBrowserWindow.Apply(snapshot, _evaluator.Evaluate(profile, snapshot)));

        DriftAssessment drift = tracker.Observe(snapshot, result, profile.UnknownGracePeriod, HoldUnknownsUntil);

        if (result.Classification == Classification.Unknown && drift.Level > EscalationLevel.None)
        {
            return EvaluateUnclassified(snapshot, drift, now);
        }

        if (result.Classification != Classification.Distracting || result.MatchedRule is null)
        {
            return MonitorEnforcement.Relaxed(snapshot, result.Classification) with
            {
                NeedsClassification = drift.NeedsClassification,
            };
        }

        Rule rule = result.MatchedRule;
        BlockScope scope = rule.Target == MatchTarget.ProcessName ? BlockScope.App : BlockScope.View;

        EnforcementAction action = ToAction(drift.Level, scope, canRedirectTab: snapshot.TabId is not null);

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

        action = Capped(action);
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

    /// <summary>Unknown past its grace period: a nudge, then the overlay at most (see <see cref="UnknownActivity"/>).</summary>
    private MonitorEnforcement EvaluateUnclassified(ActivitySnapshot snapshot, DriftAssessment drift, DateTimeOffset now)
    {
        bool isRecovering = IsRecovering(snapshot.MonitorId, now);
        EnforcementAction action = Capped(drift.Level == EscalationLevel.Nudge || isRecovering
            ? EnforcementAction.Nudge
            : EnforcementAction.Overlay);

        return new MonitorEnforcement(
            snapshot,
            Classification.Unknown,
            action,
            BlockScope.View,
            UnknownActivity.Rule,
            IsRepeatOffense: false,
            isRecovering,
            NeedsClassification: true);
    }

    // The action, no further than MaxLevel allows (Minimize and RedirectTab are the Enforce level).
    private EnforcementAction Capped(EnforcementAction action)
    {
        EnforcementAction most = MaxLevel switch
        {
            EscalationLevel.None => EnforcementAction.None,
            EscalationLevel.Nudge => EnforcementAction.Nudge,
            EscalationLevel.Overlay => EnforcementAction.Overlay,
            _ => action,
        };
        return action > most ? most : action;
    }

    private static EnforcementAction ToAction(EscalationLevel level, BlockScope scope, bool canRedirectTab)
    {
        return level switch
        {
            EscalationLevel.Nudge => EnforcementAction.Nudge,
            EscalationLevel.Overlay => EnforcementAction.Overlay,
            EscalationLevel.Enforce => EnforceAction(scope, canRedirectTab),
            _ => EnforcementAction.None,
        };
    }

    private static EnforcementAction EnforceAction(BlockScope scope, bool canRedirectTab)
    {
        if (scope == BlockScope.App)
        {
            return EnforcementAction.Minimize;
        }

        // A view block never minimizes the whole app. A browser tab goes to the block page;
        // any other view keeps the overlay until the user navigates away.
        return canRedirectTab ? EnforcementAction.RedirectTab : EnforcementAction.Overlay;
    }

    private void RememberEscalation(Guid ruleId, BlockScope scope, EnforcementAction action)
    {
        if (scope == BlockScope.App && action == EnforcementAction.Minimize)
        {
            _minimizedAppRuleIds.Add(ruleId);
        }
        else if (scope == BlockScope.View && action is (EnforcementAction.Overlay or EnforcementAction.RedirectTab))
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
        // Not the phase's start: returning from Step away moves it, and the ladder must carry on where it was. A new
        // phase always passes through a state that isn't enforcing (a break, idle), which ends the old one.
        var phase = new PhaseKey(state.Mode, state.Phase, state.ProfileId, state.CompletedFocusCycles);
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
    }

    /// <summary>Identifies one focus phase; a new value means a new phase started.</summary>
    private sealed record PhaseKey(SessionMode? Mode, SessionPhase Phase, Guid? ProfileId, int CompletedFocusCycles);
}
