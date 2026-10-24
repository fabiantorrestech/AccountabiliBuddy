using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Settings tab: quick-session shortcut, Pomodoro defaults, escalation timings, friction, unlock relocking and how
/// long looser changes wait, calendars (link check interval, Google client), the day bar, the on-screen timer (look, default, which profiles show it), the on-screen timeline, the sounds when time is up and sleep mode.
/// (Start with Windows lives in the Setup tab.)
/// Nothing applies until the one Save at the top: it saves every section that changed, after checking them all
/// (<see cref="HasUnsavedChanges"/> compares each section with what's saved; leaving the tab with changes asks first).
/// The profile-wide sections (Pomodoro, escalation, ending early, unlocks, calendars, looser changes) are locked during
/// a focus phase so friction cannot be turned off mid-session, and looser values count down first (see
/// <see cref="PendingChangesViewModel"/>); the display sections (shortcut, day bar, sleep, on-screen timer, sounds)
/// can be saved any time.
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
    private readonly NoisePlayerService? _noise;
    private StepAwayService? _stepAway;
    private readonly SessionCueService _cues;
    private readonly SleepSettingsService _sleep;

    // Set once everything is loaded, so loading doesn't count as changes.
    private bool _ready;

    /// <summary>Choices for the on-screen timer, in <see cref="OverlayStyle"/> order.</summary>
    public static IReadOnlyList<string> TimerStyleNames { get; } = new[]
    {
        "Bar with end time", "Time left with bar", "Time left only", "Ring with time left",
    };

    /// <summary>In <see cref="OverlaySize"/> order.</summary>
    public static IReadOnlyList<string> TimerSizeNames { get; } = new[] { "Small", "Medium", "Large" };

    /// <summary>In <see cref="OverlayCorner"/> order.</summary>
    public static IReadOnlyList<string> TimerCornerNames { get; } = new[] { "Bottom right", "Bottom left", "Top right", "Top left" };

    /// <summary>Division marks on the timer; the index is the number of parts, except 0 = none.</summary>
    public static IReadOnlyList<string> TimerMarkNames { get; } = new[] { "None", "Halves", "Thirds", "Quarters", "Fifths", "Sixths" };

    /// <summary>E.g. "Version 0.1.3".</summary>
    public string VersionText => $"Version {AccountabiliBuddy.App.Services.Backup.BackupService.AppVersion}";

    /// <summary>Export and import of backups (the Backup section).</summary>
    public BackupViewModel? Backup { get; init; }

    /// <summary>The Updates section.</summary>
    public UpdatesViewModel? Updates { get; init; }

    private readonly GoogleSetupGuide? _googleGuide;

    /// <summary>The step-by-step Google Cloud setup (Calendars and Sleep).</summary>
    public GoogleSetupGuide? GoogleGuide
    {
        get => _googleGuide;
        init
        {
            _googleGuide = value;
            if (value is not null)
            {
                value.ClientSaved += (_, _) =>
                {
                    GoogleClientId = _calendars.Settings.GoogleClientId ?? string.Empty;
                    OnPropertyChanged(nameof(GoogleClientSecretHint));
                };
            }
        }
    }

    // Not locked during focus: the guide only explains and saves the Google client.
    [RelayCommand]
    private void OpenGoogleGuide()
    {
        _googleGuide?.Show(includeSleep: false);
    }

    [RelayCommand]
    private void OpenSleepGuide()
    {
        _googleGuide?.Show(includeSleep: true);
    }

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
        SleepSettingsService sleep,
        string databasePath,
        NoisePlayerService? noise = null)
    {
        _noise = noise;
        // The plan's Sleep parts change the times; the on/off switch stays as edited.
        sleep.Changed += (_, _) => ShowSleepTimes();
        if (_noise is not null)
        {
            _noise.Changed += (_, _) => ShowNoiseFolder();
        }

        _sleep = sleep;
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
        LoadCountUpChoices();
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
        _configuration.Changed += (_, _) => LoadCountUpChoices(keepSelection: true);
        _quickSession.CountUpProfileChanged += (_, _) => LoadCountUpChoices();
        _configuration.PendingChanged += (_, _) => RefreshTimerProfiles();
        _overlays.Changed += (_, _) =>
        {
            ShowSavedTimerProfiles();
            ShowAppDrops(_overlays.Current);
        };

        LoadFromConfiguration();
        LoadDisplaySettings();
        LoadSoundSettings();
        LoadNoiseSettings();
        LoadSleepSettings();
        RefreshTimerProfiles();
        TimerProfiles.CollectionChanged += (_, _) => RefreshUnsaved();
        AppDrops.CollectionChanged += (_, _) => RefreshUnsaved();

        // Saved from elsewhere (a drag on the timer, the Profiles tab, a countdown ending): compare again.
        _overlays.Changed += (_, _) => RefreshUnsaved();
        _configuration.Changed += (_, _) => RefreshUnsaved();
        _dayBar.Changed += (_, _) => RefreshUnsaved();
        _calendars.Changed += (_, _) => ShowTimelineCalendars();
        TimelineRows.CollectionChanged += (_, _) => RefreshUnsaved();
        _sleep.Changed += (_, _) => RefreshUnsaved();
        _sleep.PhoneAlarmChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(PhoneBedtimeText));
            OnPropertyChanged(nameof(CanUseSmartBedtime));
            OnPropertyChanged(nameof(SmartBedtimeHint));
        };
        // Taken from the phone (ADR 35) or saved here: the switches show what's saved.
        _sleep.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(SmartWorkdays));
            OnPropertyChanged(nameof(SmartOffDays));
            OnPropertyChanged(nameof(SmartMin));
            OnPropertyChanged(nameof(SmartMax));
            OnPropertyChanged(nameof(WakeSource));
            OnPropertyChanged(nameof(PcAlarmNote));
        };
        SelectedGroupIndex = 0;
        _ready = true;
        RefreshUnsaved();
    }

    /// <summary>True when any section differs from what's saved (the header says so; leaving the tab asks first).</summary>
    [ObservableProperty]
    public partial bool HasUnsavedChanges { get; set; }

    /// <summary>The header's message after Save or Revert (e.g. "Saved." or why nothing was saved).</summary>
    [ObservableProperty]
    public partial string? SaveStatus { get; set; }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not (nameof(HasUnsavedChanges) or nameof(SaveStatus)))
        {
            RefreshUnsaved();
        }
    }

    private void RefreshUnsaved()
    {
        if (!_ready)
        {
            return;
        }

        bool unsaved = HotkeyChanged || CountUpChanged || DayBarChanged || SleepChanged || SoundsChanged || NoiseChanged || StepAwayChanged || TimerChanged || ConfigChanged;
        if (unsaved && !HasUnsavedChanges)
        {
            SaveStatus = null; // A new edit: the last save's message is old news.
        }

        HasUnsavedChanges = unsaved;
    }

    /// <summary>Saves every changed section, after checking them all; nothing is saved when one has a mistake.</summary>
    /// <returns>True when nothing is left unsaved (except profile-wide settings held back by a focus session).</returns>
    public async Task<bool> SaveAllAsync()
    {
        bool hotkey = HotkeyChanged, countUp = CountUpChanged, dayBar = DayBarChanged, sleep = SleepChanged, sounds = SoundsChanged, timer = TimerChanged;
        bool noise = NoiseChanged, stepAway = StepAwayChanged;
        bool config = ConfigChanged && !IsLocked;

        var problems = new List<string>();
        DayBarSettings? dayBarSettings = dayBar ? BuildDayBar(problems) : null;
        SleepSettings? sleepSettings = sleep ? BuildSleep() : null;
        ConfigValues? configValues = config ? BuildConfig(problems) : null;
        if (problems.Count > 0)
        {
            SaveStatus = "Not saved: " + string.Join(" ", problems);
            return false;
        }

        var notes = new List<string>();
        if (hotkey && await SaveHotkeyAsync() is string hotkeyError)
        {
            notes.Add(hotkeyError);
        }

        if (countUp)
        {
            await _quickSession.SetCountUpProfileAsync(SelectedCountUp?.ProfileId);
        }

        if (dayBarSettings is not null)
        {
            await _dayBar.SaveAsync(dayBarSettings);
        }

        if (sleepSettings is not null)
        {
            await _sleep.SaveAsync(sleepSettings);
        }

        if (sounds)
        {
            await _sounds.SaveAsync(BuildSounds());
        }

        if (noise && _noise is not null)
        {
            NoiseKeysStatus = await _noise.SaveKeysAsync(NoiseMediaKeys, NoisePlayPauseKey, NoiseVolumeUpKey, NoiseVolumeDownKey);
            if (NoiseKeysStatus is not null)
            {
                notes.Add(NoiseKeysStatus);
            }
        }

        // Step away only pauses the countdown, so it can be saved during focus too.
        if (stepAway && await SaveStepAwayAsync() is string stepAwayError)
        {
            notes.Add(stepAwayError);
        }

        if (timer)
        {
            await SaveTimerAsync();
        }

        if (configValues is not null)
        {
            notes.Add(await SaveConfigAsync(configValues));
        }
        else if (ConfigChanged && IsLocked)
        {
            notes.Add("Pomodoro, escalation, unlock and calendar changes can be saved after this focus session.");
        }

        RefreshUnsaved();
        SaveStatus = notes.Count == 0 ? "Saved." : string.Join(" ", notes.Prepend("Saved."));
        // Profile-wide changes held back by a focus session don't keep you on the tab.
        return !(HotkeyChanged || CountUpChanged || DayBarChanged || SleepChanged || SoundsChanged || NoiseChanged || StepAwayChanged || TimerChanged || (ConfigChanged && !IsLocked));
    }

    [RelayCommand]
    private Task SaveAsync()
    {
        return SaveAllAsync();
    }

    /// <summary>Puts every section back to what's saved.</summary>
    [RelayCommand]
    public void Revert()
    {
        HotkeyText = _quickSession.Preferences.Hotkey;
        LoadCountUpChoices();
        CalendarRefreshMinutes = _calendars.Settings.LinkRefreshMinutes.ToString();
        GoogleClientId = _calendars.Settings.GoogleClientId ?? string.Empty;
        GoogleClientSecret = string.Empty;
        LoadFromConfiguration();
        LoadDisplaySettings();
        LoadSoundSettings();
        LoadNoiseSettings();
        LoadStepAwaySettings();
        LoadSleepSettings();
        ShowSavedTimerProfiles();
        RefreshUnsaved();
        SaveStatus = "Changes discarded.";
    }

    private bool CountUpChanged => SelectedCountUp is not null && SelectedCountUp.ProfileId != _quickSession.CountUpProfileId;

    /// <summary>
    /// The profiles "Quick Start with Default" can start; while none is chosen (or it was deleted), the first choice is
    /// a new "QS - Default" profile with its starting rules.
    /// </summary>
    private void LoadCountUpChoices(bool keepSelection = false)
    {
        Guid? saved = _quickSession.CountUpProfileId;
        Guid? wanted = keepSelection && SelectedCountUp is not null ? SelectedCountUp.ProfileId : saved;
        CountUpChoices.Clear();
        if (_quickSession.CountUpProfileId is null)
        {
            CountUpChoices.Add(new CountUpProfileChoice(null, "QS - Default (made again with its starting rules)"));
        }

        foreach (FocusProfile profile in _configuration.Current.Profiles)
        {
            CountUpChoices.Add(new CountUpProfileChoice(profile.Id, profile.Name));
        }

        // An unsaved pick stays while it's still listed; otherwise what's saved.
        SelectedCountUp = CountUpChoices.FirstOrDefault(choice => choice.ProfileId == wanted)
            ?? CountUpChoices.FirstOrDefault(choice => choice.ProfileId == saved)
            ?? CountUpChoices.FirstOrDefault();
    }

    private bool HotkeyChanged => !string.Equals(HotkeyText.Trim(), _quickSession.Preferences.Hotkey, StringComparison.OrdinalIgnoreCase);

    private bool DayBarChanged => BuildDayBar(null) is not DayBarSettings built || built != _dayBar.Current;

    private bool SleepChanged => BuildSleep() != _sleep.Current;

    private bool SoundsChanged => _sounds.Current != BuildSounds();

    // Only the parts edited here; volume, repeat and folder change from the player itself.
    private bool NoiseChanged => _noise is not null
        && (_noise.Settings.UsesMediaKeys != NoiseMediaKeys
            || !SameKey(_noise.Settings.PlayPauseKey, NoisePlayPauseKey)
            || !SameKey(_noise.Settings.VolumeUpKey, NoiseVolumeUpKey)
            || !SameKey(_noise.Settings.VolumeDownKey, NoiseVolumeDownKey));

    private bool StepAwayChanged => _stepAway is not null
        && (!SameKey(_stepAway.Settings.Hotkey, StepAwayHotkey)
            || _stepAway.Settings.AutoAfterIdle != StepAwayAutoIdle
            || _stepAway.Settings.IdleMinutes != (int)(StepAwayIdleMinutes ?? _stepAway.Settings.IdleMinutes));

    private static bool SameKey(string saved, string edited)
    {
        return string.Equals(saved.Trim(), edited.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private bool TimerChanged => TimerProfiles.Any(row => row.IsChanged)
                                 || LookKey(_overlays.Normalized(BuildTimerLook(_overlays.Current))) != LookKey(_overlays.Normalized(_overlays.Current));

    private bool ConfigChanged
    {
        get
        {
            ConfigValues? values = BuildConfig(null);
            if (values is null)
            {
                return true; // Something typed that can't be saved yet still counts as a change.
            }

            ProfileDocument document = _configuration.Editing;
            CalendarSettings calendars = _calendars.Settings;
            return values.Pomodoro != document.PomodoroDefaults || values.Escalation != document.Escalation || values.Bypass != document.Bypass
                   || values.Locks != document.Locks || values.CalendarMinutes != calendars.LinkRefreshMinutes
                   || (GoogleClientId.Trim().Length > 0 ? GoogleClientId.Trim() : null) != calendars.GoogleClientId
                   || GoogleClientSecret.Trim().Length > 0;
        }
    }

    /// <summary>The look compared without position, per-profile choices or the order of the per-app lists.</summary>
    private static string LookKey(OverlaySettings settings)
    {
        return JsonSerializer.Serialize(settings with
        {
            X = null,
            Y = null,
            Profiles = new Dictionary<Guid, ProfileOverlay>(),
            AppDrops = new SortedDictionary<string, int>(settings.AppDrops?.ToDictionary(entry => entry.Key, entry => entry.Value) ?? new Dictionary<string, int>()),
            AppShifts = new SortedDictionary<string, int>(settings.AppShifts?.ToDictionary(entry => entry.Key, entry => entry.Value) ?? new Dictionary<string, int>()),
        });
    }

    [ObservableProperty]
    public partial bool SleepEnabled { get; set; }

    /// <summary>The phone's next alarm sets the wake time (ADR 23); saved at once.</summary>
    public bool UsePhoneAlarm
    {
        get => _sleep.Current.UsePhoneAlarm;
        set
        {
            _ = _sleep.SetUsePhoneAlarmAsync(value);
            OnPropertyChanged();
        }
    }

    /// <summary>The phone's smart bedtime sets the bedtime (ADR 34); saved at once.</summary>
    public bool UsePhoneBedtime
    {
        get => _sleep.Current.UsePhoneBedtime;
        set
        {
            _ = _sleep.SetUsePhoneBedtimeAsync(value);
            OnPropertyChanged();
        }
    }

    /// <summary>A phone is linked (paired, or its sync file received): only it measures sleep (ADR 35). Set by the app.</summary>
    public Func<bool> HasLinkedPhone { get; set; } = () => false;

    public bool CanUseSmartBedtime => HasLinkedPhone();

    public string SmartBedtimeHint => CanUseSmartBedtime
        ? "Your phone learns it from your measured sleep (Health Connect) and when you put it down; changes here reach it with the next sync."
        : "Needs your linked phone: it measures your sleep. Link it in Devices.";

    public bool SmartWorkdays
    {
        get => _sleep.Current.SmartWorkdays;
        set => SaveSmart(value, _sleep.Current.SmartOffDays, _sleep.Current.MinHours, _sleep.Current.MaxHours);
    }

    public bool SmartOffDays
    {
        get => _sleep.Current.SmartOffDays;
        set => SaveSmart(_sleep.Current.SmartWorkdays, value, _sleep.Current.MinHours, _sleep.Current.MaxHours);
    }

    /// <summary>The minimum and maximum sleep to pick from, in hours.</summary>
    public IReadOnlyList<SleepGoalChoice> SmartMinChoices { get; } =
        new[] { 5.5, 6, 6.5, 7, 7.5 }.Select(hours => new SleepGoalChoice(hours, Hours(TimeSpan.FromHours(hours)))).ToList();

    public IReadOnlyList<SleepGoalChoice> SmartMaxChoices { get; } =
        new[] { 8, 8.5, 9, 9.5, 10 }.Select(hours => new SleepGoalChoice(hours, Hours(TimeSpan.FromHours(hours)))).ToList();

    public SleepGoalChoice? SmartMin
    {
        get => SmartMinChoices.FirstOrDefault(choice => choice.Hours == _sleep.Current.MinHours);
        set
        {
            if (value is not null)
            {
                SaveSmart(_sleep.Current.SmartWorkdays, _sleep.Current.SmartOffDays, value.Hours, _sleep.Current.MaxHours);
            }
        }
    }

    public SleepGoalChoice? SmartMax
    {
        get => SmartMaxChoices.FirstOrDefault(choice => choice.Hours == _sleep.Current.MaxHours);
        set
        {
            if (value is not null)
            {
                SaveSmart(_sleep.Current.SmartWorkdays, _sleep.Current.SmartOffDays, _sleep.Current.MinHours, value.Hours);
            }
        }
    }

    /// <summary>Where the wake time comes from (ADR 35): 0 = the phone's alarm, 1 = this PC's alarm, 2 = none (the plan's).</summary>
    public int WakeSource
    {
        get => _sleep.Current.UsePcAlarm ? 1 : _sleep.Current.UsePhoneAlarm ? 0 : 2;
        set
        {
            _ = _sleep.SetWakeSourceAsync(phoneAlarm: value == 0, pcAlarm: value == 1);
            OnPropertyChanged();
            OnPropertyChanged(nameof(UsePhoneAlarm));
        }
    }

    /// <summary>This PC's alarm: whether Windows lets it wake the PC; set by the app (ADR 35).</summary>
    public Func<string?> PcAlarmWarning { get; set; } = () => null;

    public string? PcAlarmNote => WakeSource == 1 ? PcAlarmWarning() : null;

    private void SaveSmart(bool workdays, bool offDays, double minHours, double maxHours)
    {
        _ = _sleep.SetSmartAsync(workdays, offDays, minHours, maxHours);
        OnPropertyChanged(nameof(SmartWorkdays));
        OnPropertyChanged(nameof(SmartOffDays));
        OnPropertyChanged(nameof(SmartMin));
        OnPropertyChanged(nameof(SmartMax));
    }

    /// <summary>What the phone learned for each kind of night, e.g. "Workdays: 7 h 15 m in bed before waking · …".</summary>
    public string PhoneBedtimeText => _sleep.PhoneBedtime is null
        ? "No phone shares a smart bedtime yet (turn it on in the phone's Settings → Smart bedtime)."
        : $"Workdays: {AccountabiliBuddy.App.Services.Sync.PhoneBedtime.Describe(_sleep.PhoneBedtime.Workdays)}\nOff-Days: {AccountabiliBuddy.App.Services.Sync.PhoneBedtime.Describe(_sleep.PhoneBedtime.OffDays)}";

    /// <summary>The sleep goals to pick from, e.g. "8 h", "8 h 30 min".</summary>
    public IReadOnlyList<SleepGoalChoice> SleepGoalChoices { get; } =
        SleepSettings.GoalChoices.Select(hours => new SleepGoalChoice(hours, Hours(TimeSpan.FromHours(hours)))).ToList();

    /// <summary>When the "sleep if you go now" countdown starts: when going to bed would give this much sleep.</summary>
    [ObservableProperty]
    public partial SleepGoalChoice? SelectedSleepGoal { get; set; }

    /// <summary>E.g. "11:45 PM – 6:45 AM · 7 h of sleep"; set with the Sleep parts in the Day plan tab.</summary>
    [ObservableProperty]
    public partial string WeeknightSleepText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string WeekendSleepText { get; set; } = string.Empty;

    /// <summary>Opens the Day plan tab, where the sleep times are set; set by the main window.</summary>
    public Action? OpenDayPlan { get; set; }

    [RelayCommand]
    private void EditSleepTimes()
    {
        OpenDayPlan?.Invoke();
    }

    // Only the on/off switch and the goal are edited here: the times come from the day plan's Sleep parts. Display
    // only, so they can be saved during focus too.
    private SleepSettings BuildSleep()
    {
        return _sleep.Current with { Enabled = SleepEnabled, GoalHours = SelectedSleepGoal?.Hours ?? _sleep.Current.GoalHours };
    }

    /// <summary>How long a night with these times is (a bedtime before noon counts as after midnight).</summary>
    private static TimeSpan NightLength(SleepTimes times)
    {
        LocalWindow night = new SleepSchedule(times, times).Night(new DateOnly(2026, 1, 5));
        return night.End - night.Start;
    }

    private static string NightText(SleepTimes times)
    {
        return $"{times.Bedtime:t} – {times.Wake:t} · {Hours(NightLength(times))} of sleep";
    }

    private static string Hours(TimeSpan length)
    {
        return length.Minutes == 0 ? $"{(int)length.TotalHours} h" : $"{(int)length.TotalHours} h {length.Minutes} min";
    }

    private void LoadSleepSettings()
    {
        SleepEnabled = _sleep.Current.Enabled;
        SelectedSleepGoal = SleepGoalChoices.FirstOrDefault(choice => choice.Hours == _sleep.Current.GoalHours)
            ?? SleepGoalChoices.First(choice => choice.Hours == 8);
        ShowSleepTimes();
    }

    private void ShowSleepTimes()
    {
        WeeknightSleepText = NightText(_sleep.Current.Weeknights);
        WeekendSleepText = NightText(_sleep.Current.Weekends);
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

    // Sounds are display only, like the timer, so they can be saved during focus too.
    private SessionSoundSettings BuildSounds()
    {
        return _sounds.Current with
        {
            FocusEndSound = FocusEndSound?.Id ?? SoundChoice.OffId,
            BreakEndSound = BreakEndSound?.Id ?? SoundChoice.OffId,
            Volume = (int)Math.Round(SoundVolume),
        };
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
        SoundStatus = $"Added {Path.GetFileName(path)}. Pick it for either moment, then Save.";
    }

    /// <summary>The keyboard's media keys and Windows' media pop-up also control the noise player.</summary>
    [ObservableProperty]
    public partial bool NoiseMediaKeys { get; set; }

    [ObservableProperty]
    public partial string NoisePlayPauseKey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NoiseVolumeUpKey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NoiseVolumeDownKey { get; set; } = string.Empty;

    /// <summary>A shortcut that couldn't be set up, or null.</summary>
    [ObservableProperty]
    public partial string? NoiseKeysStatus { get; set; }

    /// <summary>The folder the noise player plays, and how many sound files are in it.</summary>
    [ObservableProperty]
    public partial string NoiseFolderText { get; set; } = string.Empty;

    /// <summary>Opens a folder picker; set by the view. Null result = cancelled.</summary>
    public Func<Task<string?>>? PickNoiseFolder { get; set; }

    // The folder applies at once (like adding a sound file), not with Save.
    [RelayCommand]
    private async Task ChooseNoiseFolderAsync()
    {
        if (_noise is not null && PickNoiseFolder is not null && await PickNoiseFolder() is string folder)
        {
            await _noise.ChooseFolderAsync(folder);
        }
    }

    [RelayCommand]
    private Task UseMusicNoiseFolderAsync()
    {
        return _noise?.ChooseFolderAsync(null) ?? Task.CompletedTask;
    }

    [RelayCommand]
    private void RescanNoiseFolder()
    {
        _noise?.Rescan();
    }

    private void ShowNoiseFolder()
    {
        if (_noise is not null)
        {
            NoiseFolderText = $"{_noise.Settings.EffectiveFolder} ({_noise.FileCount} sound file{(_noise.FileCount == 1 ? "" : "s")})";
        }
    }

    public bool HasNoisePlayer => _noise is not null;

    /// <summary>Work hours on this computer (ADR 27, Your day); set by the app.</summary>
    public WorkHoursViewModel? WorkHours { get; init; }

    public bool HasWorkHours => WorkHours is not null;

    /// <summary>The hyperfocus reminder card; null hides it.</summary>
    public HyperfocusViewModel? Hyperfocus { get; init; }

    public StartNudgeViewModel? StartNudge { get; init; }

    public MotionViewModel? Motion { get; init; }

    /// <summary>Lights out during work hours (design K).</summary>
    public LightsOutViewModel? LightsOut { get; init; }

    /// <summary>Play the noise when chosen headphones connect.</summary>
    public HeadphoneSettingsViewModel? Headphones { get; init; }

    public bool HasHeadphones => Headphones is not null;

    public bool HasMotion => Motion is not null;

    public bool HasStartNudge => StartNudge is not null;

    public bool HasHyperfocus => Hyperfocus is not null;

    /// <summary>Step away's shortcut and idle switch (Focus sessions); set by the app.</summary>
    public StepAwayService? StepAway
    {
        get => _stepAway;
        init
        {
            _stepAway = value;
            LoadStepAwaySettings();
            OnPropertyChanged(nameof(HasStepAway));
        }
    }

    public bool HasStepAway => _stepAway is not null;

    /// <summary>The Step away shortcut being edited; empty = none.</summary>
    [ObservableProperty]
    public partial string StepAwayHotkey { get; set; } = string.Empty;

    /// <summary>Step away by itself after a few idle minutes during a session.</summary>
    [ObservableProperty]
    public partial bool StepAwayAutoIdle { get; set; }

    [ObservableProperty]
    public partial decimal? StepAwayIdleMinutes { get; set; }

    [ObservableProperty]
    public partial string? StepAwayStatus { get; set; }

    private void LoadStepAwaySettings()
    {
        if (_stepAway is null)
        {
            return;
        }

        StepAwaySettings settings = _stepAway.Settings;
        StepAwayHotkey = settings.Hotkey;
        StepAwayAutoIdle = settings.AutoAfterIdle;
        StepAwayIdleMinutes = settings.IdleMinutes;
        StepAwayStatus = _stepAway.HotkeyError ?? StepAwayHint(settings.Hotkey);
    }

    /// <returns>Why the shortcut wasn't taken, or null.</returns>
    private async Task<string?> SaveStepAwayAsync()
    {
        if (_stepAway is null)
        {
            return null;
        }

        string? error = SameKey(_stepAway.Settings.Hotkey, StepAwayHotkey) ? null : await _stepAway.ChangeHotkeyAsync(StepAwayHotkey);
        await _stepAway.SaveAsync(_stepAway.Settings with
        {
            AutoAfterIdle = StepAwayAutoIdle,
            IdleMinutes = (int)(StepAwayIdleMinutes ?? _stepAway.Settings.IdleMinutes),
        });
        LoadStepAwaySettings();
        if (error is not null)
        {
            StepAwayStatus = error;
        }

        return error;
    }

    private static string StepAwayHint(string hotkey)
    {
        return hotkey.Length > 0
            ? $"Press {hotkey} anywhere during a session to step away, and again when you're back."
            : "No shortcut: step away from the tray or the on-screen window (hold Ctrl+Alt).";
    }

    private void LoadNoiseSettings()
    {
        if (_noise is null)
        {
            return;
        }

        NoiseSettings noise = _noise.Settings;
        NoiseMediaKeys = noise.UsesMediaKeys;
        NoisePlayPauseKey = noise.PlayPauseKey;
        NoiseVolumeUpKey = noise.VolumeUpKey;
        NoiseVolumeDownKey = noise.VolumeDownKey;
        NoiseKeysStatus = _noise.ShortcutError;
        ShowNoiseFolder();
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

    /// <summary>The side list's groups; the one at <see cref="SelectedGroupIndex"/> shows on the right.</summary>
    public IReadOnlyList<SettingsGroupViewModel> Groups { get; } = new[]
    {
        new SettingsGroupViewModel("Focus sessions", new[] { "Quick session shortcut", "Pomodoro defaults", "Escalation", "Ending a focus session early", "Unlocks", "Looser changes" }),
        new SettingsGroupViewModel("Your day", new[] { "Day bar", "Sleep", "Calendars" }),
        new SettingsGroupViewModel("On screen & sound", new[] { "Theme", "Animations", "On-screen timer", "On-screen timeline", "Sounds when time is up", "Noise player" }),
        new SettingsGroupViewModel("App & data", new[] { "Backup", "Data" }),
    };

    /// <summary>Which group shows; switching groups changes no setting.</summary>
    [ObservableProperty]
    public partial int SelectedGroupIndex { get; set; } = -1;

    partial void OnSelectedGroupIndexChanged(int value)
    {
        for (int i = 0; i < Groups.Count; i++)
        {
            Groups[i].IsSelected = i == value;
        }
    }

    /// <summary>The timeline's rows top to bottom, each shown or not.</summary>
    public ObservableCollection<TimelineRowSettingViewModel> TimelineRows { get; } = new();

    /// <summary>The visible calendars, each on the timeline's Calendar row or not.</summary>
    public ObservableCollection<TimelineCalendarViewModel> TimelineCalendars { get; } = new();

    public bool HasTimelineCalendars => TimelineCalendars.Count > 0;

    [ObservableProperty]
    public partial bool ShowColdTurkeyOnTimeline { get; set; }

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

    /// <summary>How the on-screen window looks at rest (<see cref="OverlaySettings.Quiet"/>).</summary>
    public static IReadOnlyList<string> TimerColourModes { get; } =
        ["Quiet: muted, in colour with Ctrl+Alt and in the last 2 minutes", "Full colour"];

    /// <summary>0 = quiet, 1 = full colour.</summary>
    [ObservableProperty]
    public partial int TimerColourIndex { get; set; }

    /// <summary>Quiet mode dulls the timeline's red marks and now line at rest too (<see cref="OverlaySettings.QuietMarks"/>).</summary>
    [ObservableProperty]
    public partial bool TimerQuietMarks { get; set; }

    /// <summary>The current task line shows only while Ctrl+Alt is held (<see cref="OverlaySettings.TaskOnlyWithCtrlAlt"/>).</summary>
    [ObservableProperty]
    public partial bool TaskOnlyWithCtrlAlt { get; set; }

    /// <summary>The whole on-screen window's opacity while Ctrl+Alt is held, 20–100% (<see cref="OverlaySettings.HeldOpacity"/>).</summary>
    [ObservableProperty]
    public partial double HeldOpacityPercent { get; set; } = 100;

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

    /// <summary>The default corner, as an index into <see cref="TimerCornerNames"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VerticalLabel), nameof(SidewaysLabel))]
    public partial int TimerCornerIndex { get; set; }

    private bool IsTopCornerChosen => (OverlayCorner)TimerCornerIndex is OverlayCorner.TopRight or OverlayCorner.TopLeft;

    /// <summary>"px down" from a top corner, "px up" from a bottom one.</summary>
    public string VerticalLabel => IsTopCornerChosen ? "px down" : "px up";

    /// <summary>"px left" from a right corner, "px right" from a left one.</summary>
    public string SidewaysLabel => (OverlayCorner)TimerCornerIndex is OverlayCorner.TopRight or OverlayCorner.BottomRight ? "px left" : "px right";

    partial void OnTimerCornerIndexChanged(int oldValue, int newValue)
    {
        // Switching between top and bottom: a distance still at the other side's default moves to this side's.
        bool wasTop = (OverlayCorner)oldValue is OverlayCorner.TopRight or OverlayCorner.TopLeft;
        if (wasTop != IsTopCornerChosen && MaximizedAppDrop is decimal drop
            && (int)drop == (wasTop ? OverlaySettings.TopCornerDrop : OverlaySettings.BottomCornerDrop))
        {
            MaximizedAppDrop = IsTopCornerChosen ? OverlaySettings.TopCornerDrop : OverlaySettings.BottomCornerDrop;
        }
    }

    /// <summary>Slide the on-screen window in from its corner while a maximized app is in front.</summary>
    [ObservableProperty]
    public partial bool MoveForMaximizedApps { get; set; }

    /// <summary>How far, in display pixels.</summary>
    [ObservableProperty]
    public partial decimal? MaximizedAppDrop { get; set; }

    /// <summary>Each app's own distance (set by Ctrl+Alt-dragging the timer while the app is maximized).</summary>
    public System.Collections.ObjectModel.ObservableCollection<AppDropRowViewModel> AppDrops { get; } = new();

    [ObservableProperty]
    public partial bool HasAppDrops { get; set; }

    /// <summary>Seconds the Ctrl+Alt "Hide" button hides the on-screen window.</summary>
    [ObservableProperty]
    public partial decimal? QuickHideSeconds { get; set; }

    public PendingChangesViewModel PendingChanges { get; }

    /// <summary>The quick-session shortcut being edited (recorded by pressing keys).</summary>
    [ObservableProperty]
    public partial string HotkeyText { get; set; }

    public ObservableCollection<CountUpProfileChoice> CountUpChoices { get; } = new();

    /// <summary>The profile "Quick Start with Default" starts.</summary>
    [ObservableProperty]
    public partial CountUpProfileChoice? SelectedCountUp { get; set; }

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
    public partial bool IsLocked { get; set; }

    /// <summary>How far escalation may go; set by the app (the computer types' "notice and overlay only").</summary>
    public EnforcementLimitService? EnforcementLimit { get; init; }

    /// <summary>The computer type this PC was set up as, and applying one again; set by the app.</summary>
    public AccountabiliBuddy.App.Services.Setup.ComputerTypeService? ComputerTypes { get; init; }

    /// <summary>E.g. "Set up as: Work computer", or that no type was chosen.</summary>
    public string ComputerTypeText => ComputerTypes?.Current is AccountabiliBuddy.App.Services.Setup.ComputerType type
        ? $"Set up as: {AccountabiliBuddy.App.Services.Setup.ComputerTypeService.NameOf(type)}"
        : "No computer type chosen (the setup assistant offers one).";

    /// <summary>The type to apply again: 0 Personal, 1 Work, 2 Local only.</summary>
    public int ComputerTypeIndex { get; set; }

    /// <summary>Sets the chosen type's switches again (profiles and the day plan stay as they are).</summary>
    [RelayCommand]
    private async Task ApplyComputerTypeAsync()
    {
        if (ComputerTypes is null || IsLocked)
        {
            return;
        }

        await ComputerTypes.ApplyAsync((AccountabiliBuddy.App.Services.Setup.ComputerType)Math.Clamp(ComputerTypeIndex, 0, 2));
        OnPropertyChanged(nameof(ComputerTypeText));
        OnPropertyChanged(nameof(NoticeAndOverlayOnly));
        OnPropertyChanged(nameof(KeepTitlesAndAddresses));
    }

    /// <summary>The privacy choices (History without titles and addresses); set by the app.</summary>
    public PrivacySettingsService? PrivacyChoices { get; init; }

    /// <summary>Notice and overlay only: never minimize a window or redirect a tab (e.g. on a work computer).</summary>
    public bool NoticeAndOverlayOnly
    {
        get => EnforcementLimit?.MaxLevel <= EscalationLevel.Overlay;
        set
        {
            if (EnforcementLimit is null || IsLocked || value == NoticeAndOverlayOnly)
            {
                OnPropertyChanged();
                return;
            }

            _ = EnforcementLimit.SaveAsync(value ? EscalationLevel.Overlay : EscalationLevel.Enforce);
            OnPropertyChanged();
        }
    }

    /// <summary>History keeps window titles, full addresses and screen text (encrypted); off keeps only sites and sections.</summary>
    public bool KeepTitlesAndAddresses
    {
        get => PrivacyChoices?.Current.KeepTitlesAndAddresses ?? true;
        set
        {
            if (PrivacyChoices is null || value == KeepTitlesAndAddresses)
            {
                return;
            }

            _ = PrivacyChoices.SetKeepTitlesAndAddressesAsync(value);
            OnPropertyChanged();
        }
    }

    /// <summary>The profile-wide values typed in the tab, read and checked.</summary>
    private sealed record ConfigValues(
        PomodoroSettings Pomodoro, EscalationSettings Escalation, BypassSettings Bypass, LockSettings Locks, int CalendarMinutes);

    /// <summary>Reads the profile-wide sections; null (with the reason added to <paramref name="problems"/>) when one can't be saved.</summary>
    private ConfigValues? BuildConfig(List<string>? problems)
    {
        if (!TryReadPositive(FocusMinutes, "Focus minutes", out int focus, problems)
            || !TryReadPositive(ShortBreakMinutes, "Short break minutes", out int shortBreak, problems)
            || !TryReadPositive(LongBreakMinutes, "Long break minutes", out int longBreak, problems)
            || !TryReadPositive(CyclesBeforeLongBreak, "Cycles before long break", out int cycles, problems)
            || !TryReadNonNegative(NudgeSeconds, "Nudge after", out int nudge, problems)
            || !TryReadNonNegative(OverlaySeconds, "Overlay after", out int overlay, problems)
            || !TryReadNonNegative(EnforceSeconds, "Enforce after", out int enforce, problems)
            || !TryReadNonNegative(BypassWaitSeconds, "End-early wait", out int bypassWait, problems)
            || !TryReadNonNegative(MinReasonLength, "Minimum reason length", out int reasonLength, problems)
            || !TryReadNonNegative(LooseningDelayMinutes, "Looser changes wait", out int looseningDelay, problems)
            || !TryReadPositive(CalendarRefreshMinutes, "Calendar check interval", out int calendarMinutes, problems))
        {
            return null;
        }

        if (calendarMinutes < CalendarSettings.MinLinkRefreshMinutes)
        {
            problems?.Add($"Calendar links can't be checked more often than every {CalendarSettings.MinLinkRefreshMinutes} minutes.");
            return null;
        }

        if (nudge > overlay || overlay > enforce)
        {
            problems?.Add("Escalation must go nudge ≤ overlay ≤ enforce.");
            return null;
        }

        return new ConfigValues(
            new PomodoroSettings(TimeSpan.FromMinutes(focus), TimeSpan.FromMinutes(shortBreak), TimeSpan.FromMinutes(longBreak), cycles),
            new EscalationSettings(TimeSpan.FromSeconds(nudge), TimeSpan.FromSeconds(overlay), TimeSpan.FromSeconds(enforce)),
            new BypassSettings(TimeSpan.FromSeconds(bypassWait), reasonLength),
            new LockSettings(RelockAtMidnight) { LooseningDelay = TimeSpan.FromMinutes(looseningDelay) },
            calendarMinutes);
    }

    /// <returns>What happened, for the header.</returns>
    private async Task<string> SaveConfigAsync(ConfigValues values)
    {
        PendingChanges? waiting = await _configuration.UpdateAsync(document => document with
        {
            PomodoroDefaults = values.Pomodoro, Escalation = values.Escalation, Bypass = values.Bypass, Locks = values.Locks,
        });
        SaveCalendarSettings(values.CalendarMinutes);
        StatusMessage = null;
        return _configuration.IsReadOnly
            ? "Profile-wide settings weren't saved: they are read-only (see banner)."
            : waiting is null ? string.Empty : "Looser changes wait for the countdown above.";
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

    // Changing the shortcut doesn't loosen anything, so it can be saved during focus too.
    /// <returns>Why the shortcut wasn't taken, or null.</returns>
    private async Task<string?> SaveHotkeyAsync()
    {
        string? error = await _quickSession.ChangeHotkeyAsync(HotkeyText);
        HotkeyStatus = error ?? $"Press {_quickSession.Preferences.Hotkey} anywhere to open the quick session.";
        HotkeyText = _quickSession.Preferences.Hotkey;
        return error;
    }

    // The day bar and the timer's look are display only, so they can be saved during focus too.
    private DayBarSettings? BuildDayBar(List<string>? problems)
    {
        if (DayStart is not TimeSpan start || DayEnd is not TimeSpan end)
        {
            problems?.Add("Day bar: pick a day start and a day end.");
            return null;
        }

        // Calendars not listed here (hidden ones) keep their choice.
        IEnumerable<string> listedOff = TimelineCalendars.Where(calendar => !calendar.OnTimeline).Select(calendar => calendar.Id);
        IEnumerable<string> unlistedOff = (_dayBar.Current.CalendarsOff ?? Array.Empty<string>())
            .Where(id => TimelineCalendars.All(calendar => calendar.Id != id));
        return new DayBarSettings(
            ShowDayBar,
            DayBarFullDay ? DayBarRange.FullDay : DayBarRange.FitToDay,
            TimeOnly.FromTimeSpan(start),
            TimeOnly.FromTimeSpan(end),
            TimelineRows.Select(row => row.ToChoice()).ToList(),
            listedOff.Concat(unlistedOff).Distinct().ToList(),
            ShowColdTurkeyOnTimeline);
    }

    private void MoveTimelineRow(TimelineRowSettingViewModel row, int step)
    {
        int from = TimelineRows.IndexOf(row);
        int to = from + step;
        if (from >= 0 && to >= 0 && to < TimelineRows.Count)
        {
            TimelineRows.Move(from, to);
        }
    }

    /// <summary>
    /// Lists the visible calendars with the saved choice, or the one being edited when the list is rebuilt. Left as it
    /// is when the calendars themselves didn't change (e.g. a sync status update), so ticks and focus stay put.
    /// </summary>
    private void ShowTimelineCalendars()
    {
        List<(string Id, string Name, string Source, string Color)> visible = _calendars.Sources
            .SelectMany(source => source.Calendars.Where(calendar => calendar.IsVisible)
                .Select(calendar => (calendar.Id, calendar.Name, source.Name, calendar.Color)))
            .ToList();
        if (visible.SequenceEqual(TimelineCalendars.Select(row => (row.Id, row.Name, row.SourceName, row.Color))))
        {
            return;
        }

        DayBarSettings saved = _dayBar.Current;
        var edited = TimelineCalendars.ToDictionary(calendar => calendar.Id, calendar => calendar.OnTimeline);
        TimelineCalendars.Clear();
        foreach (CalendarSource source in _calendars.Sources)
        {
            foreach (CalendarInfo calendar in source.Calendars.Where(calendar => calendar.IsVisible))
            {
                bool onTimeline = edited.TryGetValue(calendar.Id, out bool kept) ? kept : saved.ShowsCalendar(calendar.Id);
                var row = new TimelineCalendarViewModel(calendar.Id, calendar.Name, source.Name, calendar.Color, onTimeline);
                row.PropertyChanged += (_, _) => RefreshUnsaved();
                TimelineCalendars.Add(row);
            }
        }

        OnPropertyChanged(nameof(HasTimelineCalendars));
        RefreshUnsaved();
    }

    private async Task SaveTimerAsync()
    {
        // Read the rows first: saving the look shows the saved per-profile choices in them again.
        var profiles = new Dictionary<Guid, ProfileOverlay>(_overlays.Current.Profiles);
        foreach (ProfileTimerViewModel row in TimerProfiles)
        {
            profiles[row.Id] = row.ToOverlay();
        }

        await _overlays.SaveLookAsync(BuildTimerLook);
        await _overlays.SaveProfilesAsync(profiles);
        TimerStatus = null;
    }

    /// <summary>The timer's look as set in the tab, on top of <paramref name="current"/>.</summary>
    private OverlaySettings BuildTimerLook(OverlaySettings current)
    {
        return current with
        {
            Style = (OverlayStyle)Math.Clamp(TimerStyleIndex, 0, TimerStyleNames.Count - 1),
            Opacity = TimerOpacityPercent / 100,
            Size = (OverlaySize)Math.Clamp(TimerSizeIndex, 0, TimerSizeNames.Count - 1),
            Divisions = TimerMarksIndex <= 0 ? 0 : Math.Min(TimerMarksIndex, TimerMarkNames.Count - 1) + 1,
            WrapUpColor = TimerWrapUpColor,
            PulseAtMarks = TimerPulseAtMarks,
            HideInFullscreen = TimerHideInFullscreen,
            FillUp = TimerFillUp,
            Quiet = TimerColourIndex != 1,
            QuietMarks = TimerQuietMarks,
            TaskOnlyWithCtrlAlt = TaskOnlyWithCtrlAlt,
            HeldOpacity = Math.Clamp(HeldOpacityPercent, 20, 100) / 100,
            DefaultTimer = new ProfileOverlay(DefaultTimerOn, DefaultTimerColor.Hex),
            ShowTimeline = ShowTimeline,
            TimelineInFocus = TimelineStaysInFocus ? TimelineInFocus.ShowBoth : TimelineInFocus.SwapForTimer,
            TimelineShowsTime = TimelineShowsTime,
            QuickHideSeconds = (int)(QuickHideSeconds ?? OverlaySettings.Default.QuickHideSeconds),
            MoveForMaximizedApps = MoveForMaximizedApps,
            Corner = (OverlayCorner)Math.Clamp(TimerCornerIndex, 0, TimerCornerNames.Count - 1),
            MaximizedAppDrop = (int)(MaximizedAppDrop ?? OverlaySettings.Default.MaximizedAppDrop),
            AppDrops = AppDrops.ToDictionary(row => row.App, row => (int)(row.Drop ?? 0)),
            AppShifts = AppDrops.ToDictionary(row => row.App, row => (int)(row.Shift ?? 0)),
        };
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
                var added = new ProfileTimerViewModel(profile.Id, profile.Name, _overlays.Current.SavedFor(profile.Id));
                added.PropertyChanged += (_, _) => RefreshUnsaved();
                TimerProfiles.Insert(Math.Min(index, TimerProfiles.Count), added);
            }
            else
            {
                row.Name = profile.Name;
            }
        }
    }

    /// <summary>Lists each app's own distance (also after one was set by dragging the timer).</summary>
    private void ShowAppDrops(OverlaySettings overlay)
    {
        AppDrops.Clear();
        IEnumerable<string> apps = (overlay.AppDrops?.Keys ?? Enumerable.Empty<string>())
            .Concat(overlay.AppShifts?.Keys ?? Enumerable.Empty<string>())
            .Distinct()
            .Order();
        foreach (string app in apps)
        {
            var added = new AppDropRowViewModel(app, overlay.DropFor(app), overlay.ShiftFor(app), row =>
            {
                AppDrops.Remove(row);
                HasAppDrops = AppDrops.Count > 0;
                TimerStatus = $"Removed {row.App}; press Save to keep it.";
            });
            added.PropertyChanged += (_, _) => RefreshUnsaved();
            AppDrops.Add(added);
        }

        HasAppDrops = AppDrops.Count > 0;
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
        TimerStatus = "Moved back to its corner.";
    }

    [MemberNotNull(nameof(DefaultTimerColor))]
    private void LoadDisplaySettings()
    {
        DayBarSettings dayBar = _dayBar.Current;
        ShowDayBar = dayBar.Show;
        DayBarFullDay = dayBar.Range == DayBarRange.FullDay;
        DayStart = dayBar.DayStart.ToTimeSpan();
        DayEnd = dayBar.DayEnd.ToTimeSpan();
        ShowColdTurkeyOnTimeline = dayBar.ShowColdTurkey;
        TimelineRows.Clear();
        foreach (TimelineRowChoice choice in dayBar.RowsInOrder())
        {
            var row = new TimelineRowSettingViewModel(choice.Row, choice.Shown, MoveTimelineRow);
            row.PropertyChanged += (_, _) => RefreshUnsaved();
            TimelineRows.Add(row);
        }

        TimelineCalendars.Clear();
        ShowTimelineCalendars();

        OverlaySettings overlay = _overlays.Current;
        TimerStyleIndex = (int)overlay.Style;
        TimerSizeIndex = (int)overlay.Size;
        TimerMarksIndex = overlay.Divisions < 2 ? 0 : Math.Min(overlay.Divisions - 1, TimerMarkNames.Count - 1);
        TimerOpacityPercent = Math.Round(overlay.Opacity * 100);
        TimerWrapUpColor = overlay.WrapUpColor;
        TimerPulseAtMarks = overlay.PulseAtMarks;
        TimerHideInFullscreen = overlay.HideInFullscreen;
        TimerFillUp = overlay.FillUp;
        TimerColourIndex = overlay.Quiet ? 0 : 1;
        TimerQuietMarks = overlay.QuietMarks;
        TaskOnlyWithCtrlAlt = overlay.TaskOnlyWithCtrlAlt;
        HeldOpacityPercent = Math.Round(overlay.HeldOpacity * 100);
        ProfileOverlay defaultTimer = overlay.DefaultTimer ?? ProfileOverlay.Off;
        DefaultTimerOn = defaultTimer.Enabled;
        DefaultTimerColor = TimerColorOption.Match(DefaultTimerColors, defaultTimer.Color);
        ShowTimeline = overlay.ShowTimeline;
        TimelineStaysInFocus = overlay.TimelineInFocus == TimelineInFocus.ShowBoth;
        TimelineShowsTime = overlay.TimelineShowsTime;
        QuickHideSeconds = overlay.QuickHideSeconds;
        TimerCornerIndex = (int)overlay.Corner;
        MoveForMaximizedApps = overlay.MoveForMaximizedApps;
        MaximizedAppDrop = overlay.MaximizedAppDrop;
        ShowAppDrops(overlay);
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

    private static bool TryReadPositive(string text, string label, out int value, List<string>? problems)
    {
        if (int.TryParse(text.Trim(), out value) && value > 0)
        {
            return true;
        }

        problems?.Add($"{label} must be a whole number above 0.");
        return false;
    }

    private static bool TryReadNonNegative(string text, string label, out int value, List<string>? problems)
    {
        if (int.TryParse(text.Trim(), out value) && value >= 0)
        {
            return true;
        }

        problems?.Add($"{label} must be a whole number (0 or more).");
        return false;
    }
}
