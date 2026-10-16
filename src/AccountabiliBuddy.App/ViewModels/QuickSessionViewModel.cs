using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The quick-session popup: pick a profile and a length (preset or slider), optionally one break, press Enter.
/// Opens with the last choices, so starting the usual session is just the shortcut and Enter.
/// When a session is already running it shows that session instead, with "End session…".
/// The "Ends at" time is recalculated every second while the popup is open, so it follows the clock.
/// </summary>
public sealed partial class QuickSessionViewModel : ObservableObject, IDisposable
{
    public const int MinFocusMinutes = 5;
    public const int MaxFocusMinutes = 180;
    public const int MinBreakMinutes = 1;
    public const int MaxBreakMinutes = 30;

    private static readonly int[] PresetMinutes = { 15, 25, 45, 60, 90 };

    private readonly TimeProvider _timeProvider;
    private readonly DispatcherTimer _clock;

    public QuickSessionViewModel(
        IEnumerable<FocusProfile> profiles,
        QuickSessionPreferences preferences,
        bool sessionCompleted,
        string? runningSessionText,
        TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;

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
        FocusMinutes = Math.Clamp(preferences.FocusMinutes, MinFocusMinutes, MaxFocusMinutes);
        BreakEnabled = preferences.BreakEnabled;
        BreakMinutes = Math.Clamp(preferences.BreakMinutes, MinBreakMinutes, MaxBreakMinutes);

        RunningSessionText = runningSessionText;
        Heading = runningSessionText is not null ? "A session is running"
            : sessionCompleted ? "Session complete. Start another?"
            : "Quick focus";

        // Once a second is enough for a clock that shows minutes, and cheap.
        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => OnPropertyChanged(nameof(EndTimeText));
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

    /// <summary>Slider value; snapped to 5-minute steps.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FocusLabel), nameof(StartLabel), nameof(EndTimeText))]
    public partial double FocusMinutes { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartLabel), nameof(BreakLabelOpacity), nameof(EndTimeText))]
    public partial bool BreakEnabled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BreakLabel), nameof(StartLabel), nameof(EndTimeText))]
    public partial double BreakMinutes { get; set; }

    public int WholeFocusMinutes => (int)Math.Round(FocusMinutes);

    public int WholeBreakMinutes => (int)Math.Round(BreakMinutes);

    public string FocusLabel => $"{WholeFocusMinutes} min";

    public string BreakLabel => $"{WholeBreakMinutes} min";

    public double BreakLabelOpacity => BreakEnabled ? 1.0 : 0.45;

    /// <summary>When the session would end if started now, e.g. "Ends at 3:04 PM" (local time, system time format).</summary>
    public string EndTimeText
    {
        get
        {
            DateTime focusEnds = _timeProvider.GetLocalNow().DateTime.AddMinutes(WholeFocusMinutes);
            if (!BreakEnabled)
            {
                return $"Ends at {focusEnds:t}";
            }

            DateTime breakEnds = focusEnds.AddMinutes(WholeBreakMinutes);
            return $"Focus ends at {focusEnds:t} · break until {breakEnds:t}";
        }
    }

    public string StartLabel => BreakEnabled
        ? $"Start {WholeFocusMinutes} min focus + {WholeBreakMinutes} min break"
        : $"Start {WholeFocusMinutes} min focus";

    /// <summary>Raised when the user starts a session with the current choices.</summary>
    public event EventHandler? StartRequested;

    /// <summary>Raised when the user wants to end the running session (friction applies).</summary>
    public event EventHandler? EndSessionRequested;

    public event EventHandler? CloseRequested;

    public void Dispose()
    {
        _clock.Stop();
    }

    public QuickSessionPreferences ToPreferences(string hotkey)
    {
        return new QuickSessionPreferences(SelectedProfile?.Id, WholeFocusMinutes, BreakEnabled, WholeBreakMinutes, hotkey);
    }

    /// <summary>Picks preset number <paramref name="shortcutNumber"/> (1-based), as pressing that number key does.</summary>
    public void SelectPresetByNumber(int shortcutNumber)
    {
        DurationPresetViewModel? preset = Presets.FirstOrDefault(candidate => candidate.ShortcutNumber == shortcutNumber);
        if (preset is not null)
        {
            FocusMinutes = preset.Minutes;
        }
    }

    public void NudgeFocus(int deltaMinutes)
    {
        FocusMinutes = Math.Clamp(WholeFocusMinutes + deltaMinutes, MinFocusMinutes, MaxFocusMinutes);
    }

    partial void OnFocusMinutesChanged(double value)
    {
        // Snap the slider to 5-minute steps so its value always reads cleanly.
        double snapped = Math.Clamp(Math.Round(value / 5) * 5, MinFocusMinutes, MaxFocusMinutes);
        if (Math.Abs(snapped - value) > 0.001)
        {
            FocusMinutes = snapped;
            return;
        }

        foreach (DurationPresetViewModel preset in Presets)
        {
            preset.IsSelected = preset.Minutes == WholeFocusMinutes;
        }
    }

    partial void OnBreakMinutesChanged(double value)
    {
        double snapped = Math.Clamp(Math.Round(value), MinBreakMinutes, MaxBreakMinutes);
        if (Math.Abs(snapped - value) > 0.001)
        {
            BreakMinutes = snapped;
        }
    }

    private bool CanStart => !IsSessionRunning && SelectedProfile is not null;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        StartRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void SelectPreset(DurationPresetViewModel preset)
    {
        FocusMinutes = preset.Minutes;
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
}
