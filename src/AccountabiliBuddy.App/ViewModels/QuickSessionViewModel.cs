using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The quick-session popup: pick a profile and a length, optionally one break, press Enter.
/// The length is either a number of minutes (preset or slider, 5-minute steps, 1-minute with Shift) or an
/// end time ("run until 6:30 PM", typed or picked; the session then ends exactly at that time).
/// Opens with the last choices, so starting the usual session is just the shortcut and Enter.
/// When a session is already running it shows that session instead, with "End session…".
/// Times are recalculated every second while the popup is open, so they follow the clock.
/// </summary>
public sealed partial class QuickSessionViewModel : ObservableObject, IDisposable
{
    public const int MinFocusMinutes = 5;
    public const int MaxSliderMinutes = 180;
    public const int MinBreakMinutes = 1;
    public const int MaxBreakMinutes = 30;

    private const int CoarseStep = 5;
    private const int FineStep = 1;

    private static readonly int[] PresetMinutes = { 15, 25, 45, 60, 90 };

    private readonly TimeProvider _timeProvider;
    private readonly DispatcherTimer _clock;

    // Length in minutes when no end time is set (also what gets remembered for next time).
    private int _durationMinutes;

    // Set when the user chose "run until"; the session then ends exactly at this local time.
    private DateTime? _untilTime;

    // Guards against the picker's own change notification re-applying a time we just set.
    private bool _settingPickerTime;

    public QuickSessionViewModel(
        IEnumerable<FocusProfile> profiles,
        QuickSessionPreferences preferences,
        bool sessionCompleted,
        string? runningSessionText,
        TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        UntilText = string.Empty;
        CountUpLabel = "Count up";

        foreach (FocusProfile profile in profiles)
        {
            ProfileOptions.Add(ProfileOption.From(profile));
        }

        for (int index = 0; index < PresetMinutes.Length; index++)
        {
            Presets.Add(new DurationPresetViewModel(PresetMinutes[index], index + 1));
        }

        SelectedProfile = ProfileOptions.FirstOrDefault(option => option.Id == preferences.ProfileId)
            ?? ProfileOptions.FirstOrDefault();
        SetDuration(preferences.FocusMinutes);
        BreakEnabled = preferences.BreakEnabled;
        BreakMinutes = Math.Clamp(preferences.BreakMinutes, MinBreakMinutes, MaxBreakMinutes);

        RunningSessionText = runningSessionText;
        Heading = runningSessionText is not null ? "A session is running"
            : sessionCompleted ? "Session complete. Start another?"
            : "Quick focus";

        // Once a second is enough for a clock that shows minutes, and cheap.
        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => OnClockTick();
        _clock.Start();
    }

    public string Heading { get; }

    /// <summary>Set when a session is already running; the popup then shows it instead of the start controls.</summary>
    public string? RunningSessionText { get; }

    public bool IsSessionRunning => RunningSessionText is not null;

    public bool CanChoose => !IsSessionRunning;

    public bool HasProfiles => ProfileOptions.Count > 0;

    public bool HasNoProfiles => !HasProfiles && !IsSessionRunning;

    public ObservableCollection<ProfileOption> ProfileOptions { get; } = new();

    public ObservableCollection<DurationPresetViewModel> Presets { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial ProfileOption? SelectedProfile { get; set; }

    /// <summary>True while Shift is held: the slider and arrow keys move in 1-minute steps.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SliderStep))]
    public partial bool FineAdjust { get; set; }

    public double SliderStep => FineAdjust ? FineStep : CoarseStep;

    /// <summary>Slider position. Longer "run until" sessions park the slider at its end.</summary>
    public double SliderMinutes
    {
        get => Math.Min(DisplayMinutes, MaxSliderMinutes);
        set
        {
            int step = FineAdjust ? FineStep : CoarseStep;
            int snapped = (int)(Math.Round(value / step) * step);
            if (snapped != (int)Math.Round(SliderMinutes))
            {
                SetDuration(snapped);
            }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartLabel), nameof(BreakLabelOpacity), nameof(EndTimeText))]
    public partial bool BreakEnabled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BreakLabel), nameof(StartLabel), nameof(EndTimeText))]
    public partial double BreakMinutes { get; set; }

    public bool IsUntilMode => _untilTime is not null;

    /// <summary>How long focus would last if started now.</summary>
    public TimeSpan FocusLength => _untilTime is DateTime until
        ? until - Now
        : TimeSpan.FromMinutes(_durationMinutes);

    /// <summary>Whole minutes shown on the label (rounded up in "run until" mode, so 36 min 20 s shows as 37).</summary>
    public int DisplayMinutes => (int)Math.Ceiling(Math.Max(0, FocusLength.TotalMinutes));

    public int WholeBreakMinutes => (int)Math.Round(BreakMinutes);

    public string FocusLabel => FormatMinutes(DisplayMinutes);

    public string BreakLabel => $"{WholeBreakMinutes} min";

    public double BreakLabelOpacity => BreakEnabled ? 1.0 : 0.45;

    public string StartLabel
    {
        get
        {
            string focus = _untilTime is DateTime until
                ? $"Start focus until {until:t}"
                : $"Start {FormatMinutes(_durationMinutes)} focus";
            return BreakEnabled ? $"{focus} + {WholeBreakMinutes} min break" : focus;
        }
    }

    /// <summary>"Ends at 3:04 PM", or "Runs until 6:30 PM" for a chosen end time; clickable to choose one.</summary>
    public string EndTimeText
    {
        get
        {
            DateTime focusEnds = _untilTime ?? Now.AddMinutes(_durationMinutes);
            string focusPart = IsUntilMode ? $"Runs until {focusEnds:t}" : $"Ends at {focusEnds:t}";
            if (!BreakEnabled)
            {
                return focusPart;
            }

            DateTime breakEnds = focusEnds.AddMinutes(WholeBreakMinutes);
            string focusPrefix = IsUntilMode ? "Focus runs until" : "Focus ends at";
            return $"{focusPrefix} {focusEnds:t} · break until {breakEnds:t}";
        }
    }

    /// <summary>True while the end-time editor (typing box + picker) is open.</summary>
    [ObservableProperty]
    public partial bool IsEditingUntil { get; set; }

    [ObservableProperty]
    public partial string UntilText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUntilError))]
    public partial string? UntilError { get; set; }

    public bool HasUntilError => UntilError is not null;

    /// <summary>Time of day chosen in the clock picker; picking one applies it right away.</summary>
    [ObservableProperty]
    public partial TimeSpan? PickerTime { get; set; }

    private DateTime Now => _timeProvider.GetLocalNow().DateTime;

    /// <summary>Raised when the user starts a session with the current choices.</summary>
    public event EventHandler? StartRequested;

    /// <summary>Raised when the user starts the Quick Start profile with no set end (the on-screen bar counts up).</summary>
    public event EventHandler? CountUpRequested;

    /// <summary>E.g. "Count up · Quick Start".</summary>
    [ObservableProperty]
    public partial string CountUpLabel { get; set; }

    /// <summary>Raised when the user wants to end the running session (friction applies).</summary>
    public event EventHandler? EndSessionRequested;

    public event EventHandler? CloseRequested;

    public void Dispose()
    {
        _clock.Stop();
    }

    public QuickSessionPreferences ToPreferences(string hotkey)
    {
        // A chosen end time is a one-off; the remembered default stays a plain length.
        return new QuickSessionPreferences(SelectedProfile?.Id, _durationMinutes, BreakEnabled, WholeBreakMinutes, hotkey);
    }

    /// <summary>Picks preset number <paramref name="shortcutNumber"/> (1-based), as pressing that number key does.</summary>
    public void SelectPresetByNumber(int shortcutNumber)
    {
        DurationPresetViewModel? preset = Presets.FirstOrDefault(candidate => candidate.ShortcutNumber == shortcutNumber);
        if (preset is not null)
        {
            SetDuration(preset.Minutes);
        }
    }

    /// <summary>Arrow keys: ±5 minutes, or ±1 with Shift.</summary>
    public void NudgeFocus(int direction, bool fine)
    {
        int step = fine ? FineStep : CoarseStep;
        int current = Math.Min(DisplayMinutes, MaxSliderMinutes);
        int target = fine ? current + (direction * step) : (int)(Math.Round((double)current / step) * step) + (direction * step);
        SetDuration(target);
    }

    partial void OnBreakMinutesChanged(double value)
    {
        double snapped = Math.Clamp(Math.Round(value), MinBreakMinutes, MaxBreakMinutes);
        if (Math.Abs(snapped - value) > 0.001)
        {
            BreakMinutes = snapped;
        }
    }

    partial void OnPickerTimeChanged(TimeSpan? value)
    {
        if (_settingPickerTime || value is not TimeSpan time)
        {
            return;
        }

        // The picker gives a time of day; treat it like a typed 24-hour time (next occurrence, within 12 h).
        UntilText = $"{(int)time.TotalHours:00}:{time.Minutes:00}";
        ApplyUntil();
    }

    private bool CanStart => !IsSessionRunning && SelectedProfile is not null && FocusLength >= TimeSpan.FromMinutes(1);

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        StartRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool CanCountUp => !IsSessionRunning;

    [RelayCommand(CanExecute = nameof(CanCountUp))]
    private void CountUp()
    {
        CountUpRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void SelectPreset(DurationPresetViewModel preset)
    {
        SetDuration(preset.Minutes);
    }

    [RelayCommand]
    private void BeginEditUntil()
    {
        DateTime currentEnd = _untilTime ?? Now.AddMinutes(_durationMinutes);
        UntilText = currentEnd.ToString("t");
        UntilError = null;

        _settingPickerTime = true;
        PickerTime = new TimeSpan(currentEnd.Hour, currentEnd.Minute, 0);
        _settingPickerTime = false;

        IsEditingUntil = true;
    }

    [RelayCommand]
    private void ApplyUntil()
    {
        DateTime now = Now;
        if (!ClockTimeParser.TryParseUpcoming(UntilText, now, out DateTime target, out string? error))
        {
            UntilError = error;
            return;
        }

        if (target - now < TimeSpan.FromMinutes(1))
        {
            UntilError = "That's less than a minute away.";
            return;
        }

        _untilTime = target;
        UntilError = null;
        IsEditingUntil = false;
        RaiseLengthChanged();
    }

    [RelayCommand]
    private void CancelEditUntil()
    {
        IsEditingUntil = false;
        UntilError = null;
    }

    [RelayCommand]
    private void EndSession()
    {
        EndSessionRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Switches to a plain length (leaving "run until" mode).</summary>
    private void SetDuration(int minutes)
    {
        _durationMinutes = Math.Clamp(minutes, MinFocusMinutes, MaxSliderMinutes);
        _untilTime = null;
        RaiseLengthChanged();
    }

    private void OnClockTick()
    {
        // A chosen end time that has (almost) arrived can no longer be started; fall back to the plain length.
        if (_untilTime is DateTime until && until - Now < TimeSpan.FromMinutes(1))
        {
            _untilTime = null;
        }

        RaiseLengthChanged();
    }

    private void RaiseLengthChanged()
    {
        OnPropertyChanged(nameof(IsUntilMode));
        OnPropertyChanged(nameof(SliderMinutes));
        OnPropertyChanged(nameof(FocusLabel));
        OnPropertyChanged(nameof(StartLabel));
        OnPropertyChanged(nameof(EndTimeText));
        StartCommand.NotifyCanExecuteChanged();

        foreach (DurationPresetViewModel preset in Presets)
        {
            preset.IsSelected = !IsUntilMode && preset.Minutes == _durationMinutes;
        }
    }

    /// <summary>"90 min" within the slider's range; longer "run until" lengths as "4 h 10 min".</summary>
    private static string FormatMinutes(int minutes)
    {
        if (minutes <= MaxSliderMinutes)
        {
            return $"{minutes} min";
        }

        return minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes / 60} h {minutes % 60} min";
    }
}
