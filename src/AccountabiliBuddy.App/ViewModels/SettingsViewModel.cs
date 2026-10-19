using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Settings tab: quick-session shortcut, Pomodoro defaults, escalation timings, friction, unlock relocking and how
/// long looser changes wait, calendars (link check interval, Google client), the day bar, the on-screen timer (look, default, which profiles show it), the on-screen timeline and the sounds when time is up.
/// (Start with Windows lives in the Setup tab.)
/// Saving is locked during a focus phase so friction cannot be turned off mid-session; looser values count down
/// first and the tab shows them as saved (see <see cref="PendingChangesViewModel"/>).
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ConfigurationService _configuration;
    private readonly SessionController _sessions;
    private readonly QuickSessionService _quickSession;
    private readonly CalendarService _calendars;
    private readonly IDataProtector _protector;
    private readonly DayBarSettingsService _dayBar;
    private readonly OverlaySettingsService _overlays;
    private readonly SessionSoundSettingsService _sounds;
    private readonly SessionCueService _cues;

    /// <summary>Choices for the on-screen timer, in <see cref="OverlayStyle"/> order.</summary>
    public static IReadOnlyList<string> TimerStyleNames { get; } = new[]
    {
        "Bar with end time", "Time left with bar", "Time left only", "Ring with time left",
    };

    /// <summary>In <see cref="OverlaySize"/> order.</summary>
    public static IReadOnlyList<string> TimerSizeNames { get; } = new[] { "Small", "Medium", "Large" };

    /// <summary>Division marks on the timer; the index is the number of parts, except 0 = none.</summary>
    public static IReadOnlyList<string> TimerMarkNames { get; } = new[] { "None", "Halves", "Thirds", "Quarters", "Fifths", "Sixths" };

    /// <summary>E.g. "Version 0.1.3".</summary>
    public string VersionText => $"Version {AccountabiliBuddy.App.Services.Backup.BackupService.AppVersion}";

    /// <summary>Export and import of backups (the Backup section).</summary>
    public BackupViewModel? Backup { get; init; }

    public SettingsViewModel(
        ConfigurationService configuration,
        SessionController sessions,
        QuickSessionService quickSession,
        CalendarService calendars,
        IDataProtector protector,
        PendingChangesViewModel pendingChanges,
        DayBarSettingsService dayBar,
        OverlaySettingsService overlays,
        SessionSoundSettingsService sounds,
        SessionCueService cues,
        string databasePath)
    {
        _sounds = sounds;
        _cues = cues;
        _dayBar = dayBar;
        _overlays = overlays;
        _configuration = configuration;
        _sessions = sessions;
        _quickSession = quickSession;
        _calendars = calendars;
        _protector = protector;
        PendingChanges = pendingChanges;
        HotkeyText = quickSession.Preferences.Hotkey;
        HotkeyStatus = quickSession.HotkeyError ?? $"Press {quickSession.Preferences.Hotkey} anywhere to open the quick session.";
        DatabasePath = databasePath;

        FocusMinutes = ShortBreakMinutes = LongBreakMinutes = CyclesBeforeLongBreak = string.Empty;
        NudgeSeconds = OverlaySeconds = EnforceSeconds = string.Empty;
        BypassWaitSeconds = MinReasonLength = LooseningDelayMinutes = string.Empty;
        CalendarRefreshMinutes = calendars.Settings.LinkRefreshMinutes.ToString();
        GoogleClientId = calendars.Settings.GoogleClientId ?? string.Empty;
        GoogleClientSecret = string.Empty;

        _sessions.StateChanged += (_, _) => IsLocked = _sessions.IsEnforcing;
        IsLocked = _sessions.IsEnforcing;
        _configuration.PendingCancelled += (_, _) =>
        {
            LoadFromConfiguration();
            StatusMessage = "Looser changes cancelled.";
        };

        // The per-profile timer list follows the profiles and the Profiles tab's timer choices.
        _configuration.Changed += (_, _) => RefreshTimerProfiles();
        _configuration.PendingChanged += (_, _) => RefreshTimerProfiles();
        _overlays.Changed += (_, _) => ShowSavedTimerProfiles();

        LoadFromConfiguration();
        LoadDisplaySettings();
        LoadSoundSettings();
        RefreshTimerProfiles();
    }

    /// <summary>Set by the view: asks for a .wav file, null when cancelled.</summary>
    public Func<Task<string?>>? PickSoundFile { get; set; }

    /// <summary>Every sound that can be picked (see <see cref="SoundChoice"/>).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<SoundChoice> SoundChoices { get; set; }

    [ObservableProperty]
    public partial SoundChoice? FocusEndSound { get; set; }

    [ObservableProperty]
    public partial SoundChoice? BreakEndSound { get; set; }

    /// <summary>0–100.</summary>
    [ObservableProperty]
    public partial double SoundVolume { get; set; }

    [ObservableProperty]
    public partial string? SoundStatus { get; set; }

    // Not locked during focus: sounds are display only, like the timer.
    [RelayCommand]
    private async Task ApplySoundsAsync()
    {
        await _sounds.SaveAsync(_sounds.Current with
        {
            FocusEndSound = FocusEndSound?.Id ?? SoundChoice.OffId,
            BreakEndSound = BreakEndSound?.Id ?? SoundChoice.OffId,
            Volume = (int)Math.Round(SoundVolume),
        });
        SoundStatus = "Saved.";
    }

    [RelayCommand]
    private void TestFocusEndSound()
    {
        _cues.Play(FocusEndSound?.Id ?? SoundChoice.OffId, (int)Math.Round(SoundVolume));
    }

    [RelayCommand]
    private void TestBreakEndSound()
    {
        _cues.Play(BreakEndSound?.Id ?? SoundChoice.OffId, (int)Math.Round(SoundVolume));
    }

    /// <summary>Adds a .wav file to the choices (one at a time; a new one replaces it) and picks it for the end of focus.</summary>
    [RelayCommand]
    private async Task AddSoundFileAsync()
    {
        string? path = PickSoundFile is null ? null : await PickSoundFile();
        if (path is null)
        {
            return;
        }

        if (!path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            SoundStatus = "Only .wav files can be played.";
            return;
        }

        string? breakId = BreakEndSound?.Id;
        await _sounds.SaveAsync(_sounds.Current with { CustomFile = path });
        SoundChoices = SoundChoice.List(path);
        FocusEndSound = SoundChoices.First(choice => choice.Id == SoundChoice.FilePrefix + path);
        BreakEndSound = SoundChoices.FirstOrDefault(choice => choice.Id == breakId) ?? SoundChoices[0];
        SoundStatus = $"Added {Path.GetFileName(path)}. Pick it for either moment, then Apply.";
    }

    [MemberNotNull(nameof(SoundChoices))]
    private void LoadSoundSettings()
    {
        SessionSoundSettings sounds = _sounds.Current;
        SoundChoices = SoundChoice.List(sounds.CustomFile);
        FocusEndSound = SoundChoices.FirstOrDefault(choice => choice.Id == sounds.FocusEndSound) ?? SoundChoices[0];
        BreakEndSound = SoundChoices.FirstOrDefault(choice => choice.Id == sounds.BreakEndSound) ?? SoundChoices[0];
        SoundVolume = sounds.Volume;
    }

    /// <summary>Each profile's timer on/off and colour (also set in the profile's editor).</summary>
    public ObservableCollection<ProfileTimerViewModel> TimerProfiles { get; } = new();

    public string DatabasePath { get; }

    [ObservableProperty]
    public partial bool ShowDayBar { get; set; }

    /// <summary>True = midnight to midnight; false = fit to the day start and end.</summary>
    [ObservableProperty]
    public partial bool DayBarFullDay { get; set; }

    [ObservableProperty]
    public partial TimeSpan? DayStart { get; set; }

    [ObservableProperty]
    public partial TimeSpan? DayEnd { get; set; }

    [ObservableProperty]
    public partial string? DayBarStatus { get; set; }

    [ObservableProperty]
    public partial int TimerStyleIndex { get; set; }

    [ObservableProperty]
    public partial int TimerSizeIndex { get; set; }

    [ObservableProperty]
    public partial int TimerMarksIndex { get; set; }

    [ObservableProperty]
    public partial bool TimerWrapUpColor { get; set; }

    [ObservableProperty]
    public partial bool TimerPulseAtMarks { get; set; }

    [ObservableProperty]
    public partial bool TimerHideInFullscreen { get; set; }

    [ObservableProperty]
    public partial bool TimerFillUp { get; set; }

    /// <summary>20–100.</summary>
    [ObservableProperty]
    public partial double TimerOpacityPercent { get; set; }

    [ObservableProperty]
    public partial string? TimerStatus { get; set; }

    /// <summary>The timer for profiles set to "Use default".</summary>
    [ObservableProperty]
    public partial bool DefaultTimerOn { get; set; }

    [ObservableProperty]
    public partial TimerColorOption DefaultTimerColor { get; set; }

    public IReadOnlyList<TimerColorOption> DefaultTimerColors { get; } = TimerColorOption.ForDefault();

    /// <summary>The all-day timeline on days with focus blocks.</summary>
    [ObservableProperty]
    public partial bool ShowTimeline { get; set; }

    /// <summary>True = the timeline stays during sessions with the timer under it; false = the timer takes its place.</summary>
    [ObservableProperty]
    public partial bool TimelineStaysInFocus { get; set; }

    [ObservableProperty]
    public partial bool TimelineShowsTime { get; set; }

    /// <summary>Seconds the Ctrl+Alt "Hide" button hides the on-screen window.</summary>
    [ObservableProperty]
    public partial decimal? QuickHideSeconds { get; set; }

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

    /// <summary>How often iCal links are checked (5 minutes or more).</summary>
    [ObservableProperty]
    public partial string CalendarRefreshMinutes { get; set; }

    [ObservableProperty]
    public partial string GoogleClientId { get; set; }

    /// <summary>Typed to replace the saved secret; left empty, the saved one is kept.</summary>
    [ObservableProperty]
    public partial string GoogleClientSecret { get; set; }

    public string GoogleClientSecretHint => _calendars.Settings.ProtectedGoogleClientSecret is null
        ? "Client secret"
        : "Client secret (saved; type to replace)";

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
            || !TryReadNonNegative(LooseningDelayMinutes, "Looser changes wait", out int looseningDelay)
            || !TryReadPositive(CalendarRefreshMinutes, "Calendar check interval", out int calendarMinutes))
        {
            return;
        }

        if (calendarMinutes < CalendarSettings.MinLinkRefreshMinutes)
        {
            StatusMessage = $"Not saved: calendar links can't be checked more often than every {CalendarSettings.MinLinkRefreshMinutes} minutes.";
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
        SaveCalendarSettings(calendarMinutes);
        StatusMessage = _configuration.IsReadOnly
            ? "Not saved: settings are read-only (see banner)."
            : waiting is null ? "Saved." : "Saved. Looser changes wait for the countdown above.";
    }

    /// <summary>Calendar settings are this computer's own (not part of the shared profile file) and never loosen anything.</summary>
    private void SaveCalendarSettings(int calendarMinutes)
    {
        string? secret = GoogleClientSecret.Trim().Length > 0
            ? Convert.ToBase64String(_protector.Protect(GoogleClientSecret.Trim()))
            : _calendars.Settings.ProtectedGoogleClientSecret;
        _calendars.SaveSettings(new CalendarSettings(
            calendarMinutes,
            GoogleClientId.Trim().Length > 0 ? GoogleClientId.Trim() : null,
            secret));
        GoogleClientSecret = string.Empty;
        OnPropertyChanged(nameof(GoogleClientSecretHint));
    }

    // Not locked during focus: changing the shortcut does not loosen anything.
    [RelayCommand]
    private async Task ApplyHotkeyAsync()
    {
        string? error = await _quickSession.ChangeHotkeyAsync(HotkeyText);
        HotkeyStatus = error ?? $"Saved. Press {_quickSession.Preferences.Hotkey} anywhere to open the quick session.";
        HotkeyText = _quickSession.Preferences.Hotkey;
    }

    // Not locked during focus: the day bar and the timer's look are display only.
    [RelayCommand]
    private async Task ApplyDayBarAsync()
    {
        if (DayStart is not TimeSpan start || DayEnd is not TimeSpan end)
        {
            DayBarStatus = "Not saved: pick a day start and a day end.";
            return;
        }

        await _dayBar.SaveAsync(new DayBarSettings(
            ShowDayBar,
            DayBarFullDay ? DayBarRange.FullDay : DayBarRange.FitToDay,
            TimeOnly.FromTimeSpan(start),
            TimeOnly.FromTimeSpan(end)));
        DayBarStatus = "Saved.";
    }

    [RelayCommand]
    private async Task ApplyTimerAsync()
    {
        // Read the rows first: saving the look shows the saved per-profile choices in them again.
        var profiles = new Dictionary<Guid, ProfileOverlay>(_overlays.Current.Profiles);
        foreach (ProfileTimerViewModel row in TimerProfiles)
        {
            profiles[row.Id] = row.ToOverlay();
        }

        await _overlays.SaveLookAsync(current => current with
        {
            Style = (OverlayStyle)Math.Clamp(TimerStyleIndex, 0, TimerStyleNames.Count - 1),
            Opacity = TimerOpacityPercent / 100,
            Size = (OverlaySize)Math.Clamp(TimerSizeIndex, 0, TimerSizeNames.Count - 1),
            Divisions = TimerMarksIndex <= 0 ? 0 : Math.Min(TimerMarksIndex, TimerMarkNames.Count - 1) + 1,
            WrapUpColor = TimerWrapUpColor,
            PulseAtMarks = TimerPulseAtMarks,
            HideInFullscreen = TimerHideInFullscreen,
            FillUp = TimerFillUp,
            DefaultTimer = new ProfileOverlay(DefaultTimerOn, DefaultTimerColor.Hex),
            ShowTimeline = ShowTimeline,
            TimelineInFocus = TimelineStaysInFocus ? TimelineInFocus.ShowBoth : TimelineInFocus.SwapForTimer,
            TimelineShowsTime = TimelineShowsTime,
            QuickHideSeconds = (int)(QuickHideSeconds ?? OverlaySettings.Default.QuickHideSeconds),
        });
        await _overlays.SaveProfilesAsync(profiles);
        TimerStatus = "Saved.";
    }

    /// <summary>Adds rows for new profiles, renames and drops the others; choices not applied yet are kept.</summary>
    private void RefreshTimerProfiles()
    {
        IReadOnlyList<FocusProfile> profiles = _configuration.Editing.Profiles;
        foreach (ProfileTimerViewModel gone in TimerProfiles.Where(row => profiles.All(profile => profile.Id != row.Id)).ToList())
        {
            TimerProfiles.Remove(gone);
        }

        for (int index = 0; index < profiles.Count; index++)
        {
            FocusProfile profile = profiles[index];
            ProfileTimerViewModel? row = TimerProfiles.FirstOrDefault(candidate => candidate.Id == profile.Id);
            if (row is null)
            {
                TimerProfiles.Insert(Math.Min(index, TimerProfiles.Count), new ProfileTimerViewModel(profile.Id, profile.Name, _overlays.Current.SavedFor(profile.Id)));
            }
            else
            {
                row.Name = profile.Name;
            }
        }
    }

    private void ShowSavedTimerProfiles()
    {
        foreach (ProfileTimerViewModel row in TimerProfiles)
        {
            row.ShowOverlay(_overlays.Current.SavedFor(row.Id));
        }
    }

    [RelayCommand]
    private async Task ResetTimerPositionAsync()
    {
        await _overlays.SavePositionAsync(null, null);
        TimerStatus = "Moved back to the top-right corner.";
    }

    [MemberNotNull(nameof(DefaultTimerColor))]
    private void LoadDisplaySettings()
    {
        DayBarSettings dayBar = _dayBar.Current;
        ShowDayBar = dayBar.Show;
        DayBarFullDay = dayBar.Range == DayBarRange.FullDay;
        DayStart = dayBar.DayStart.ToTimeSpan();
        DayEnd = dayBar.DayEnd.ToTimeSpan();

        OverlaySettings overlay = _overlays.Current;
        TimerStyleIndex = (int)overlay.Style;
        TimerSizeIndex = (int)overlay.Size;
        TimerMarksIndex = overlay.Divisions < 2 ? 0 : Math.Min(overlay.Divisions - 1, TimerMarkNames.Count - 1);
        TimerOpacityPercent = Math.Round(overlay.Opacity * 100);
        TimerWrapUpColor = overlay.WrapUpColor;
        TimerPulseAtMarks = overlay.PulseAtMarks;
        TimerHideInFullscreen = overlay.HideInFullscreen;
        TimerFillUp = overlay.FillUp;
        ProfileOverlay defaultTimer = overlay.DefaultTimer ?? ProfileOverlay.Off;
        DefaultTimerOn = defaultTimer.Enabled;
        DefaultTimerColor = TimerColorOption.Match(DefaultTimerColors, defaultTimer.Color);
        ShowTimeline = overlay.ShowTimeline;
        TimelineStaysInFocus = overlay.TimelineInFocus == TimelineInFocus.ShowBoth;
        TimelineShowsTime = overlay.TimelineShowsTime;
        QuickHideSeconds = overlay.QuickHideSeconds;
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
