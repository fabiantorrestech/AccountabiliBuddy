using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The app mode (ADR 68): AccountabiliBuddy (guards you) or ProductaBuddy (companion only), in Settings › App &amp; data › App mode
/// and in the setup assistant. Switching to ProductaBuddy waits the looser-change delay and is locked during a session; back is at once.
/// Use from the UI thread only.
/// </summary>
public sealed partial class GuidanceModeViewModel : ObservableObject
{
    private const int GuardingChoice = 0;
    private const int CompanionChoice = 1;

    private readonly GuidanceModeService _service;
    private readonly SessionController _sessions;
    private bool _loading;

    public GuidanceModeViewModel(GuidanceModeService service, SessionController sessions)
    {
        _service = service;
        _sessions = sessions;
        InFocus = sessions.State.IsActive;
        sessions.StateChanged += (_, state) => InFocus = state.IsActive;
        sessions.Ticked += (_, _) => RefreshPending();
        service.Changed += (_, _) => Load();
        Load();
    }

    public static IReadOnlyList<string> ChoiceNames { get; } =
        [$"{AppBrand.ProductName}: guards you", $"{AppBrand.CompanionName}: companion only"];

    /// <summary>The two modes by name.</summary>
    public static IReadOnlyList<string> ShortChoiceNames { get; } = [AppBrand.ProductName, AppBrand.CompanionName];

    /// <summary>How many of Settings' AccountabiliBuddy-only groups and rows ProductaBuddy hides (Steer me to start's nudge,
    /// work hours, bedtime reminders, escalation, keeping titles and addresses, screen reading).</summary>
    public const int HiddenInCompanion = 6;

    /// <summary>The App mode switch's title (Settings › App &amp; data): on is ProductaBuddy, off AccountabiliBuddy.</summary>
    public static string ToggleTitle => $"Use {AppBrand.CompanionName}";

    /// <summary>The differences between the modes, editable under the switch (ADR 69). Set by the app.</summary>
    [ObservableProperty]
    public partial ModeCompareViewModel? Compare { get; set; }

    /// <summary>The App mode switch: on while ProductaBuddy is in effect or on its way.</summary>
    public bool IsCompanionOn
    {
        get => ChoiceIndex == CompanionChoice;
        set => ChoiceIndex = value ? CompanionChoice : GuardingChoice;
    }

    /// <summary>"5 AccountabiliBuddy-only settings are hidden" while ProductaBuddy is in effect; empty otherwise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHiddenNote))]
    public partial string HiddenNote { get; set; } = string.Empty;

    public bool HasHiddenNote => HiddenNote.Length > 0;

    /// <summary>A switch waits for the running session to end: "now" can still be paid for (what ending the session early costs).</summary>
    [ObservableProperty]
    public partial bool CanSwitchNow { get; set; }

    [RelayCommand]
    private async Task SwitchNowAsync()
    {
        if (SwitchToCompanion is not null)
        {
            await SwitchToCompanion();
        }

        Load();
    }

    [ObservableProperty]
    public partial int ChoiceIndex { get; set; }

    /// <summary>A focus session is running: picking ProductaBuddy asks when (after it, or now at the cost of ending it early).</summary>
    [ObservableProperty]
    public partial bool InFocus { get; set; }

    /// <summary>
    /// Switching to ProductaBuddy with its dialogs (during a session: after it, or now at what ending it early costs). Set by the app;
    /// null asks the service directly (the usual wait; refused during a session).
    /// </summary>
    public Func<Task>? SwitchToCompanion { get; set; }

    /// <summary>"ProductaBuddy in 8:12 …"; empty when nothing waits.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPending), nameof(CanKeep))]
    public partial string PendingText { get; set; } = string.Empty;

    public bool HasPending => PendingText.Length > 0;

    /// <summary>What the chosen mode does, under the choice.</summary>
    [ObservableProperty]
    public partial string Explanation { get; set; } = string.Empty;

    /// <summary>The fresh install's default nobody chose yet: linked devices that chose already decide.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanKeep))]
    public partial bool IsDefault { get; set; }

    /// <summary>"Keep ProductaBuddy" (or AccountabiliBuddy): makes the starting default a real choice, sent to the other devices.</summary>
    public bool CanKeep => IsDefault && !HasPending;

    public string KeepText => $"Keep {(_service.IsCompanion ? AppBrand.CompanionName : AppBrand.ProductName)}";

    [RelayCommand]
    private Task KeepAsync() => _service.ConfirmDefaultAsync();

    /// <summary>When a paid "now" (its end-early wait, ADR 66) will start the switch's own wait; null when none is armed. Set by the app.</summary>
    public Func<DateTimeOffset?>? ArmedSwitchAt { get; set; }

    /// <summary>Cancels an armed "now". Set by the app.</summary>
    public Action? CancelArmedSwitch { get; set; }

    /// <summary>Keeps guarding: cancels the waiting switch, and an armed "now" with it.</summary>
    [RelayCommand]
    private Task CancelAsync()
    {
        CancelArmedSwitch?.Invoke();
        return _service.ChooseGuardingAsync();
    }

    partial void OnChoiceIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsCompanionOn));
        if (!_loading && value >= 0)
        {
            _ = ChooseAsync(value == CompanionChoice);
        }
    }

    /// <summary>The setup assistant's page was answered with the mode shown (counts as set up; nothing is sent).</summary>
    public Task MarkAskedAsync() => _service.MarkAskedAsync();

    /// <summary>The setup assistant's choice: the same rules as here.</summary>
    public Task ChooseAsync(bool companion)
    {
        return companion ? RequestCompanionAsync() : _service.ChooseGuardingAsync();
    }

    private async Task RequestCompanionAsync()
    {
        if (_service.IsCompanion)
        {
            return;
        }

        if (SwitchToCompanion is not null)
        {
            await SwitchToCompanion();
        }
        else
        {
            await _service.RequestCompanionAsync();
        }

        Load(); // Cancelled or refused: the choice shows the mode again.
    }

    private void Load()
    {
        _loading = true;
        bool companionShown = _service.IsCompanion || _service.PendingAt is not null;
        ChoiceIndex = companionShown ? CompanionChoice : GuardingChoice;
        IsDefault = !_service.State.IsChosen;
        OnPropertyChanged(nameof(KeepText));
        OnPropertyChanged(nameof(CanKeep));
        HiddenNote = _service.IsCompanion
            ? $"{HiddenInCompanion} {AppBrand.ProductName}-only settings are hidden while {AppBrand.CompanionName} is in effect."
            : string.Empty;
        Explanation = _service.IsCompanion
            ? $"{AppBrand.CompanionName} keeps sessions, your day, tasks, stats, sounds and the on-screen window, and your Cold Turkey and Curbox blocks. It doesn't watch your screens, nudge, minimize or lock anything else, on any of your devices."
            : $"{AppBrand.ProductName} also watches what's on your screens and steps in when you drift: notices, the overlay, minimizing, work hours, Pre-Work and bedtime reminders.";
        RefreshPending();
        _loading = false;
    }

    private void RefreshPending()
    {
        CanSwitchNow = _sessions.State.IsActive && _service.State.Pending is { DuringSession: false } && SwitchToCompanion is not null;
        string Clock(DateTimeOffset at) => $"{ArmedEndRules.Countdown(at - _sessions.Now)} ({TimeZoneInfo.ConvertTime(at, TimeZoneInfo.Local).ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture)}";
        PendingText = _service.State.Pending switch
        {
            { DuringSession: true } now => $"{AppBrand.CompanionName} in {Clock(now.ApplyAt)}), with this session still running. Until then everything still guards you.",
            GuidancePending later when _sessions.State.IsActive && later.ApplyAt <= _sessions.Now
                => $"{AppBrand.CompanionName} when this session ends. Until then everything still guards you.",
            GuidancePending later => $"{AppBrand.CompanionName} in {Clock(later.ApplyAt)}, or after a session running then). Until then everything still guards you.",
            _ when ArmedSwitchAt?.Invoke() is DateTimeOffset armedAt
                => $"Switching to {AppBrand.CompanionName}: what ending this session early costs runs until {Clock(armedAt)}), then the usual wait.",
            _ => string.Empty,
        };
    }
}
