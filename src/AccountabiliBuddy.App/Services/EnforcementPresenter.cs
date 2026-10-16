using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.App.Views;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Ports;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Turns enforcement decisions into what the user sees: a toast per monitor, a dimmed overlay per monitor,
/// minimize requests, and one "is this on track?" prompt at a time.
/// Only acts on changes, so it can be called every second. Use from the UI thread only.
/// </summary>
public sealed class EnforcementPresenter
{
    // A minimize request is not repeated for the same window sooner than this, while Windows carries it out.
    private static readonly TimeSpan MinimizeRetryInterval = TimeSpan.FromSeconds(2);
    private const int MaxTitleLength = 80;

    private readonly IEnforcer _enforcer;
    private readonly IMonitorLocator _monitors;
    private readonly TimeProvider _timeProvider;

    private readonly Dictionary<string, MonitorPresentation> _presentationByMonitor = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _lastMinimizeByWindow = new(StringComparer.Ordinal);

    private ClassifyPrompt? _prompt;
    private string? _promptProcessName;

    public EnforcementPresenter(IEnforcer enforcer, IMonitorLocator monitors, TimeProvider timeProvider)
    {
        _enforcer = enforcer;
        _monitors = monitors;
        _timeProvider = timeProvider;
    }

    /// <summary>Raised with the monitor id when the user asks for time to navigate away from a blocked view.</summary>
    public event EventHandler<string>? RecoveryRequested;

    /// <summary>Raised when the user answers the classify prompt.</summary>
    public event EventHandler<Rule>? RuleCreated;

    /// <summary>Raised with the process name when the user picks "Not now" on the classify prompt.</summary>
    public event EventHandler<string>? ClassificationDeferred;

    /// <param name="profileName">Name of the session's profile, shown on the overlay and prompt.</param>
    /// <param name="sessionDetail">Time left and session type, shown on the overlay.</param>
    public void Apply(IReadOnlyList<MonitorEnforcement> decisions, string profileName, string sessionDetail)
    {
        foreach (MonitorEnforcement decision in decisions)
        {
            ApplyToMonitor(decision, profileName, sessionDetail);
        }

        UpdatePrompt(decisions, profileName);
    }

    /// <summary>Closes everything shown on a monitor that was disconnected.</summary>
    public void ForgetMonitor(string monitorId)
    {
        if (_presentationByMonitor.Remove(monitorId, out MonitorPresentation? presentation))
        {
            CloseToast(presentation);
            CloseOverlay(presentation);
        }
    }

    public void CloseAll()
    {
        foreach (MonitorPresentation presentation in _presentationByMonitor.Values)
        {
            CloseToast(presentation);
            CloseOverlay(presentation);
        }

        _presentationByMonitor.Clear();
        ClosePrompt();
    }

    private void ApplyToMonitor(MonitorEnforcement decision, string profileName, string sessionDetail)
    {
        if (!_presentationByMonitor.TryGetValue(decision.MonitorId, out MonitorPresentation? presentation))
        {
            presentation = new MonitorPresentation(decision.MonitorId);
            _presentationByMonitor[decision.MonitorId] = presentation;
        }

        EnforcementAction previousAction = presentation.LastAction;
        presentation.LastAction = decision.Action;

        switch (decision.Action)
        {
            case EnforcementAction.None:
                // Back on track: a nudge is no longer relevant, but a "minimized" notice stays until it times out.
                if (presentation.ToastKind == ToastKind.Nudge)
                {
                    CloseToast(presentation);
                }

                CloseOverlay(presentation);
                break;

            case EnforcementAction.Nudge:
                CloseOverlay(presentation);
                if (previousAction == EnforcementAction.None)
                {
                    ShowNudge(presentation, decision, profileName);
                }

                break;

            case EnforcementAction.Overlay:
                if (presentation.ToastKind == ToastKind.Nudge)
                {
                    CloseToast(presentation);
                }

                ShowOrUpdateOverlay(presentation, decision, profileName, sessionDetail);
                break;

            case EnforcementAction.Minimize:
                CloseOverlay(presentation);
                if (RequestMinimize(decision.Snapshot.WindowId))
                {
                    ShowMinimizedNotice(presentation, decision);
                }

                break;
        }
    }

    private void ShowNudge(MonitorPresentation presentation, MonitorEnforcement decision, string profileName)
    {
        string target = DescribeTarget(decision);
        string message = decision.Scope == BlockScope.App
            ? $"{target} isn't part of this session."
            : $"This view isn't part of this session: {target}";

        // Minimizing is the quick way back for a whole-app block; for a view the user navigates away themselves.
        NudgeToastViewModel viewModel = decision.Scope == BlockScope.App
            ? new NudgeToastViewModel($"Off track · {profileName}", message, "Back to work",
                () => MinimizeNow(decision.Snapshot.WindowId))
            : new NudgeToastViewModel($"Off track · {profileName}", message, actionLabel: null, onAction: null);

        ShowToast(presentation, viewModel, ToastKind.Nudge);
    }

    private void ShowMinimizedNotice(MonitorPresentation presentation, MonitorEnforcement decision)
    {
        string target = DescribeTarget(decision);
        NudgeToastViewModel viewModel = decision.IsRepeatOffense
            ? new NudgeToastViewModel(
                $"{target} is blocked until your next break",
                "Minimized again.",
                actionLabel: null,
                onAction: null)
            : new NudgeToastViewModel(
                $"Minimized {target}",
                "Reopening it before your next break minimizes it right away.",
                actionLabel: null,
                onAction: null);

        ShowToast(presentation, viewModel, ToastKind.Minimized);
    }

    private void ShowToast(MonitorPresentation presentation, NudgeToastViewModel viewModel, ToastKind kind)
    {
        CloseToast(presentation);

        MonitorArea? area = _monitors.Find(presentation.MonitorId);
        if (area is null)
        {
            return;
        }

        var toast = new NudgeToast { DataContext = viewModel };
        MonitorPlacement.PlaceInCorner(toast, area, MonitorPlacement.Corner.TopRight);
        toast.Closed += (_, _) =>
        {
            // The toast may close itself (timeout or button); forget it only if it is still the current one.
            if (presentation.Toast == toast)
            {
                presentation.Toast = null;
                presentation.ToastKind = ToastKind.None;
            }
        };

        presentation.Toast = toast;
        presentation.ToastKind = kind;
        toast.Show();
    }

    private void ShowOrUpdateOverlay(
        MonitorPresentation presentation, MonitorEnforcement decision, string profileName, string sessionDetail)
    {
        if (presentation.Overlay is null)
        {
            MonitorArea? area = _monitors.Find(presentation.MonitorId);
            if (area is null)
            {
                return;
            }

            presentation.OverlayViewModel = new DriftOverlayViewModel();
            var overlay = new DriftOverlay { DataContext = presentation.OverlayViewModel };
            MonitorPlacement.Cover(overlay, area);
            presentation.Overlay = overlay;
            overlay.Show();
        }

        DriftOverlayViewModel viewModel = presentation.OverlayViewModel!;
        viewModel.Target = DescribeTarget(decision);
        viewModel.SessionDetail = $"{profileName} · {sessionDetail}";
        // Kept for as long as this overlay stays open, even once normal drift timing catches up.
        if (decision.IsRepeatOffense)
        {
            viewModel.Hint = "You were already warned about this earlier in this focus block.";
        }

        string windowId = decision.Snapshot.WindowId;
        if (decision.Scope == BlockScope.View)
        {
            int seconds = (int)EnforcementEngine.RecoveryWindow.TotalSeconds;
            viewModel.SetActions(
                $"Navigate away ({seconds} s)",
                () => RecoveryRequested?.Invoke(this, presentation.MonitorId),
                "Minimize app",
                () => MinimizeNow(windowId));
        }
        else
        {
            viewModel.SetActions("Back to work", () => MinimizeNow(windowId), null, null);
        }
    }

    private void UpdatePrompt(IReadOnlyList<MonitorEnforcement> decisions, string profileName)
    {
        if (_prompt is not null)
        {
            bool stillNeeded = decisions.Any(decision => decision.NeedsClassification
                && string.Equals(decision.Snapshot.ProcessName, _promptProcessName, StringComparison.OrdinalIgnoreCase));
            if (!stillNeeded)
            {
                ClosePrompt();
            }

            return;
        }

        MonitorEnforcement? unknown = decisions.FirstOrDefault(decision => decision.NeedsClassification);
        if (unknown is null)
        {
            return;
        }

        MonitorArea? area = _monitors.Find(unknown.MonitorId);
        if (area is null)
        {
            return;
        }

        string processName = unknown.Snapshot.ProcessName;
        var viewModel = new ClassifyPromptViewModel(profileName, processName, unknown.Snapshot.WindowTitle);
        viewModel.Answered += (_, rule) =>
        {
            ClosePrompt();
            RuleCreated?.Invoke(this, rule);
        };
        viewModel.Deferred += (_, _) =>
        {
            ClosePrompt();
            ClassificationDeferred?.Invoke(this, processName);
        };

        var prompt = new ClassifyPrompt { DataContext = viewModel };
        MonitorPlacement.PlaceInCorner(prompt, area, MonitorPlacement.Corner.BottomRight);
        prompt.Closed += (_, _) =>
        {
            // Closed some other way (e.g. Alt+F4): treat it like "Not now" instead of asking again next second.
            if (_prompt == prompt)
            {
                ClosePrompt();
                ClassificationDeferred?.Invoke(this, processName);
            }
        };
        _prompt = prompt;
        _promptProcessName = processName;
        prompt.Show();
    }

    private void ClosePrompt()
    {
        ClassifyPrompt? prompt = _prompt;
        _prompt = null;
        _promptProcessName = null;
        prompt?.Close();
    }

    private void MinimizeNow(string windowId)
    {
        _lastMinimizeByWindow[windowId] = _timeProvider.GetUtcNow();
        _ = _enforcer.MinimizeWindowAsync(windowId);
    }

    /// <returns>True if a new minimize request was sent; false if one was sent moments ago.</returns>
    private bool RequestMinimize(string windowId)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (_lastMinimizeByWindow.TryGetValue(windowId, out DateTimeOffset last) && now - last < MinimizeRetryInterval)
        {
            return false;
        }

        // Drop stale entries so the map does not grow over a long session.
        foreach (string staleId in _lastMinimizeByWindow.Where(entry => now - entry.Value >= MinimizeRetryInterval)
                     .Select(entry => entry.Key).ToList())
        {
            _lastMinimizeByWindow.Remove(staleId);
        }

        MinimizeNow(windowId);
        return true;
    }

    private static void CloseToast(MonitorPresentation presentation)
    {
        NudgeToast? toast = presentation.Toast;
        presentation.Toast = null;
        presentation.ToastKind = ToastKind.None;
        toast?.Close();
    }

    private static void CloseOverlay(MonitorPresentation presentation)
    {
        DriftOverlay? overlay = presentation.Overlay;
        presentation.Overlay = null;
        presentation.OverlayViewModel = null;
        overlay?.Close();
    }

    private static string DescribeTarget(MonitorEnforcement decision)
    {
        ActivitySnapshot snapshot = decision.Snapshot;
        if (decision.Scope != BlockScope.View || string.IsNullOrWhiteSpace(snapshot.WindowTitle))
        {
            return snapshot.ProcessName;
        }

        string title = snapshot.WindowTitle.Length > MaxTitleLength
            ? snapshot.WindowTitle[..MaxTitleLength] + "…"
            : snapshot.WindowTitle;
        return $"{snapshot.ProcessName} · {title}";
    }

    private enum ToastKind
    {
        None,
        Nudge,
        Minimized,
    }

    /// <summary>What is currently shown on one monitor.</summary>
    private sealed class MonitorPresentation(string monitorId)
    {
        public string MonitorId { get; } = monitorId;

        public EnforcementAction LastAction { get; set; } = EnforcementAction.None;

        public NudgeToast? Toast { get; set; }

        public ToastKind ToastKind { get; set; } = ToastKind.None;

        public DriftOverlay? Overlay { get; set; }

        public DriftOverlayViewModel? OverlayViewModel { get; set; }
    }
}
