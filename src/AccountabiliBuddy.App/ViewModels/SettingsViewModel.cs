using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Settings tab: Pomodoro defaults, escalation timings and friction. (Start with Windows lives in the Setup tab.)
/// Saving is locked during a focus phase so friction cannot be turned off mid-session.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ConfigurationService _configuration;
    private readonly SessionController _sessions;

    public SettingsViewModel(ConfigurationService configuration, SessionController sessions, string databasePath)
    {
        _configuration = configuration;
        _sessions = sessions;
        DatabasePath = databasePath;

        FocusMinutes = ShortBreakMinutes = LongBreakMinutes = CyclesBeforeLongBreak = string.Empty;
        NudgeSeconds = OverlaySeconds = EnforceSeconds = string.Empty;
        BypassWaitSeconds = MinReasonLength = string.Empty;

        _sessions.StateChanged += (_, _) => IsLocked = _sessions.IsEnforcing;
        IsLocked = _sessions.IsEnforcing;

        LoadFromConfiguration();
    }

    public string DatabasePath { get; }

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
            || !TryReadNonNegative(MinReasonLength, "Minimum reason length", out int reasonLength))
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

        await _configuration.UpdateAsync(document =>
            document with { PomodoroDefaults = pomodoro, Escalation = escalation, Bypass = bypass });
        StatusMessage = _configuration.IsReadOnly ? "Not saved: settings are read-only (see banner)." : "Saved.";
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Revert()
    {
        LoadFromConfiguration();
        StatusMessage = "Unsaved changes discarded.";
    }

    private void LoadFromConfiguration()
    {
        ProfileDocument document = _configuration.Current;

        FocusMinutes = document.PomodoroDefaults.FocusLength.TotalMinutes.ToString("0");
        ShortBreakMinutes = document.PomodoroDefaults.ShortBreakLength.TotalMinutes.ToString("0");
        LongBreakMinutes = document.PomodoroDefaults.LongBreakLength.TotalMinutes.ToString("0");
        CyclesBeforeLongBreak = document.PomodoroDefaults.CyclesBeforeLongBreak.ToString();

        NudgeSeconds = document.Escalation.NudgeAfter.TotalSeconds.ToString("0");
        OverlaySeconds = document.Escalation.OverlayAfter.TotalSeconds.ToString("0");
        EnforceSeconds = document.Escalation.EnforceAfter.TotalSeconds.ToString("0");

        BypassWaitSeconds = document.Bypass.WaitDuration.TotalSeconds.ToString("0");
        MinReasonLength = document.Bypass.MinReasonLength.ToString();
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
