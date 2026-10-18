using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Settings tab: quick-session shortcut, Pomodoro defaults, escalation timings, friction, unlock relocking and how
/// long looser changes wait. (Start with Windows lives in the Setup tab.)
/// Saving is locked during a focus phase so friction cannot be turned off mid-session; looser values count down
/// first and the tab shows them as saved (see <see cref="PendingChangesViewModel"/>).
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ConfigurationService _configuration;
    private readonly SessionController _sessions;
    private readonly QuickSessionService _quickSession;

    public SettingsViewModel(
        ConfigurationService configuration,
        SessionController sessions,
        QuickSessionService quickSession,
        PendingChangesViewModel pendingChanges,
        string databasePath)
    {
        _configuration = configuration;
        _sessions = sessions;
        _quickSession = quickSession;
        PendingChanges = pendingChanges;
        HotkeyText = quickSession.Preferences.Hotkey;
        HotkeyStatus = quickSession.HotkeyError ?? $"Press {quickSession.Preferences.Hotkey} anywhere to open the quick session.";
        DatabasePath = databasePath;

        FocusMinutes = ShortBreakMinutes = LongBreakMinutes = CyclesBeforeLongBreak = string.Empty;
        NudgeSeconds = OverlaySeconds = EnforceSeconds = string.Empty;
        BypassWaitSeconds = MinReasonLength = LooseningDelayMinutes = string.Empty;

        _sessions.StateChanged += (_, _) => IsLocked = _sessions.IsEnforcing;
        IsLocked = _sessions.IsEnforcing;
        _configuration.PendingCancelled += (_, _) =>
        {
            LoadFromConfiguration();
            StatusMessage = "Looser changes cancelled.";
        };

        LoadFromConfiguration();
    }

    public string DatabasePath { get; }

    public PendingChangesViewModel PendingChanges { get; }

    /// <summary>The quick-session shortcut being edited (recorded by pressing keys).</summary>
    [ObservableProperty]
    public partial string HotkeyText { get; set; }

    [ObservableProperty]
    public partial string HotkeyStatus { get; set; }

    [ObservableProperty]
    public partial string FocusMinutes { get; set; }

    [ObservableProperty]
    public partial string ShortBreakMinutes { get; set; }

    [ObservableProperty]
    public partial string LongBreakMinutes { get; set; }

    [ObservableProperty]
    public partial string CyclesBeforeLongBreak { get; set; }

    [ObservableProperty]
    public partial string NudgeSeconds { get; set; }

    [ObservableProperty]
    public partial string OverlaySeconds { get; set; }

    [ObservableProperty]
    public partial string EnforceSeconds { get; set; }

    [ObservableProperty]
    public partial string BypassWaitSeconds { get; set; }

    [ObservableProperty]
    public partial string MinReasonLength { get; set; }

    /// <summary>Unlocks relock at midnight unless another time is picked when unlocking.</summary>
    [ObservableProperty]
    public partial bool RelockAtMidnight { get; set; }

    /// <summary>How long looser changes wait before they take effect.</summary>
    [ObservableProperty]
    public partial string LooseningDelayMinutes { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(RevertCommand))]
    public partial bool IsLocked { get; set; }

    private bool CanEdit => !IsLocked;

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task SaveAsync()
    {
        if (!TryReadPositive(FocusMinutes, "Focus minutes", out int focus)
            || !TryReadPositive(ShortBreakMinutes, "Short break minutes", out int shortBreak)
            || !TryReadPositive(LongBreakMinutes, "Long break minutes", out int longBreak)
            || !TryReadPositive(CyclesBeforeLongBreak, "Cycles before long break", out int cycles)
            || !TryReadNonNegative(NudgeSeconds, "Nudge after", out int nudge)
            || !TryReadNonNegative(OverlaySeconds, "Overlay after", out int overlay)
            || !TryReadNonNegative(EnforceSeconds, "Enforce after", out int enforce)
            || !TryReadNonNegative(BypassWaitSeconds, "End-early wait", out int bypassWait)
            || !TryReadNonNegative(MinReasonLength, "Minimum reason length", out int reasonLength)
            || !TryReadNonNegative(LooseningDelayMinutes, "Looser changes wait", out int looseningDelay))
        {
            return;
        }

        if (nudge > overlay || overlay > enforce)
        {
            StatusMessage = "Not saved: escalation must go nudge ≤ overlay ≤ enforce.";
            return;
        }

        var escalation = new EscalationSettings(
            TimeSpan.FromSeconds(nudge), TimeSpan.FromSeconds(overlay), TimeSpan.FromSeconds(enforce));
        var pomodoro = new PomodoroSettings(
            TimeSpan.FromMinutes(focus), TimeSpan.FromMinutes(shortBreak), TimeSpan.FromMinutes(longBreak), cycles);
        var bypass = new BypassSettings(TimeSpan.FromSeconds(bypassWait), reasonLength);

        var locks = new LockSettings(RelockAtMidnight) { LooseningDelay = TimeSpan.FromMinutes(looseningDelay) };

        PendingChanges? waiting = await _configuration.UpdateAsync(document =>
            document with { PomodoroDefaults = pomodoro, Escalation = escalation, Bypass = bypass, Locks = locks });
        StatusMessage = _configuration.IsReadOnly
            ? "Not saved: settings are read-only (see banner)."
            : waiting is null ? "Saved." : "Saved. Looser changes wait for the countdown above.";
    }

    // Not locked during focus: changing the shortcut does not loosen anything.
    [RelayCommand]
    private async Task ApplyHotkeyAsync()
    {
        string? error = await _quickSession.ChangeHotkeyAsync(HotkeyText);
        HotkeyStatus = error ?? $"Saved. Press {_quickSession.Preferences.Hotkey} anywhere to open the quick session.";
        HotkeyText = _quickSession.Preferences.Hotkey;
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Revert()
    {
        LoadFromConfiguration();
        StatusMessage = "Unsaved changes discarded.";
    }

    private void LoadFromConfiguration()
    {
        ProfileDocument document = _configuration.Editing;

        FocusMinutes = document.PomodoroDefaults.FocusLength.TotalMinutes.ToString("0");
        ShortBreakMinutes = document.PomodoroDefaults.ShortBreakLength.TotalMinutes.ToString("0");
        LongBreakMinutes = document.PomodoroDefaults.LongBreakLength.TotalMinutes.ToString("0");
        CyclesBeforeLongBreak = document.PomodoroDefaults.CyclesBeforeLongBreak.ToString();

        NudgeSeconds = document.Escalation.NudgeAfter.TotalSeconds.ToString("0");
        OverlaySeconds = document.Escalation.OverlayAfter.TotalSeconds.ToString("0");
        EnforceSeconds = document.Escalation.EnforceAfter.TotalSeconds.ToString("0");

        BypassWaitSeconds = document.Bypass.WaitDuration.TotalSeconds.ToString("0");
        MinReasonLength = document.Bypass.MinReasonLength.ToString();

        RelockAtMidnight = document.Locks.RelockAtMidnight;
        LooseningDelayMinutes = document.Locks.LooseningDelay.TotalMinutes.ToString("0");
    }

    private bool TryReadPositive(string text, string label, out int value)
    {
        if (int.TryParse(text.Trim(), out value) && value > 0)
        {
            return true;
        }

        StatusMessage = $"Not saved: {label} must be a whole number above 0.";
        return false;
    }

    private bool TryReadNonNegative(string text, string label, out int value)
    {
        if (int.TryParse(text.Trim(), out value) && value >= 0)
        {
            return true;
        }

        StatusMessage = $"Not saved: {label} must be a whole number (0 or more).";
        return false;
    }
}
