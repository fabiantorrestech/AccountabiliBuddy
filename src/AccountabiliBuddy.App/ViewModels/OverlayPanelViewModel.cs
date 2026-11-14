using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The Ctrl+Alt panel (10c, 14b): what is running and Done / Pause / +10 min, this session's tasks, the sound, and the footer. Everything it
/// does goes through the overlay model's commands and the app's actions, which are the Today page's own, so no gate is bypassed. Compact,
/// Standard and Large differ only in width and in what they list. Use from the UI thread only.
/// </summary>
public sealed partial class OverlayPanelViewModel : ObservableObject
{
    public const double CompactWidth = 320;
    public const double StandardWidth = 400;
    public const double LargeWidth = 760;

    private readonly OverlaySettingsService _settings;
    private readonly SessionController _sessions;
    private LunchEndPromptViewModel? _watchedPrompt;

    public OverlayPanelViewModel(TaskbarOverlayViewModel model, OverlaySettingsService settings, SessionController sessions)
    {
        Model = model;
        _settings = settings;
        _sessions = sessions;
        HeaderSub = TasksHeader = SwitchHint = PauseText = string.Empty;
        Size = settings.Current.PanelSize;
        // The lunch row follows lunch while the panel is open: its time left ticks, and Count / Discard here or on another device takes it away.
        _sessions.Ticked += (_, _) => RefreshLunchRow();
        settings.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(PeekKeysLocked));
            OnPropertyChanged(nameof(ShowsSafeHints));
            OnPropertyChanged(nameof(PeekKeysTip));
            OnPropertyChanged(nameof(HideShortcutOn));
            OnPropertyChanged(nameof(ShowsHideKey));
            OnPropertyChanged(nameof(CompactHintText));
            OnPropertyChanged(nameof(HideTip));
        };
    }

    /// <summary>The panel is the sticky one (Ctrl+Alt+Space), which takes every key by focus; false while it only peeks. Set by the overlay when it opens.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsAllHints), nameof(ShowsSafeHints), nameof(CompactHintText), nameof(ShowsHideKey))]
    public partial bool IsSticky { get; set; }

    /// <summary>A peek registers no keys (<see cref="OverlaySettings.PeekKeysLocked"/>); read live so the panel and Settings stay in step.</summary>
    public bool PeekKeysLocked => _settings.Current.PeekKeysLocked;

    /// <summary>Letter hints for every key show only where every key works: the sticky panel.</summary>
    public bool ShowsAllHints => IsSticky;

    /// <summary>Hints for the safe keys (hide, +10 min) also show in an unlocked peek.</summary>
    public bool ShowsSafeHints => IsSticky || !PeekKeysLocked;

    public string CompactHintText => IsSticky
        ? $"D done   P pause   T +10   B side task   H {HideKeyWord}"
        : $"T +10   H {HideKeyWord}";

    /// <summary>Ctrl+Alt+H hides the overlay until it is pressed again, and so does the panel's H key; with that switch off, H is the short Hide.</summary>
    public bool HideShortcutOn => _settings.Current.OverlayOn && _settings.Current.HideShortcutOn;

    private string HideKeyWord => HideShortcutOn ? "hide/show" : "hide";

    /// <summary>The "H" next to the Hide button: only where H is that button's own key (the Ctrl+Alt+H shortcut is off), and where the safe keys work.</summary>
    public bool ShowsHideKey => ShowsSafeHints && !HideShortcutOn;

    public string HideTip => HideShortcutOn
        ? $"Hide for {_settings.Current.QuickHideSeconds} seconds. Ctrl+Alt+H hides it until you press it again."
        : $"Hide for {_settings.Current.QuickHideSeconds} seconds";

    /// <summary>Disable is refused during a focus session, like the Settings switch (the same rule as Settings: locked while a phase enforces).</summary>
    public bool CanDisable => !_sessions.IsEnforcing;

    public bool DisableLocked => !CanDisable;

    public string DisableTip => CanDisable
        ? "Turn the overlay off. Today or Settings turns it back on."
        : "Can't turn the overlay off during a focus session";

    /// <summary>The lock button's tip.</summary>
    public string PeekKeysTip => PeekKeysLocked
        ? "Peek keys are off: Ctrl+Alt held registers no shortcuts. Click to turn them on."
        : "Peek keys are on (hide, +10 min, up, down). Click to turn them off.";

    [RelayCommand]
    private void TogglePeekKeys()
    {
        _ = _settings.SaveLookAsync(settings => settings with { PeekKeysLocked = !settings.PeekKeysLocked });
    }

    public TaskbarOverlayViewModel Model { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Width), nameof(IsCompact), nameof(IsLarge), nameof(ShowsSteps), nameof(ShowsSideTaskButton))]
    public partial PanelSize Size { get; set; }

    public double Width => Size switch
    {
        PanelSize.Compact => CompactWidth,
        PanelSize.Large => LargeWidth,
        _ => StandardWidth,
    };

    public bool IsCompact => Size == PanelSize.Compact;

    public bool IsLarge => Size == PanelSize.Large;

    /// <summary>Compact lists no steps under the current task.</summary>
    public bool ShowsSteps => Size != PanelSize.Compact;

    /// <summary>"left · ends 2:20 PM".</summary>
    [ObservableProperty]
    public partial string HeaderSub { get; set; }

    [ObservableProperty]
    public partial bool InSession { get; set; }

    [ObservableProperty]
    public partial bool ShowsExtend { get; set; }

    [ObservableProperty]
    public partial string PauseText { get; set; }

    /// <summary>"This session", or "Start focus on" between sessions.</summary>
    [ObservableProperty]
    public partial string TasksHeader { get; set; }

    /// <summary>"1–3 to switch", or "1–3 to start".</summary>
    [ObservableProperty]
    public partial string SwitchHint { get; set; }

    /// <summary>Planning stage: "I'm ready" ends the planning minutes now (not while a first session learns).</summary>
    [ObservableProperty]
    public partial bool ShowsReady { get; set; }

    /// <summary>Today's deep work against the goal ("Goal · 40m of 1h"); empty hides it.</summary>
    [ObservableProperty]
    public partial string GoalText { get; set; } = string.Empty;

    public bool HasQr => Model.Actions.ShowSyncQr is not null;

    [ObservableProperty]
    public partial bool ShowsLunch { get; set; }

    /// <summary>Lunch is running: the panel's lunch row (its time left and the ✕) shows (LU1b, ADR 88).</summary>
    [ObservableProperty]
    public partial bool ShowsLunchRow { get; set; }

    /// <summary>"Lunch · 42 min left".</summary>
    [ObservableProperty]
    public partial string LunchLine { get; set; } = string.Empty;

    /// <summary>The "End lunch?" question the lunch row's ✕ opens, shown under the row; null without one.</summary>
    [ObservableProperty]
    public partial LunchEndPromptViewModel? LunchPrompt { get; set; }

    [ObservableProperty]
    public partial bool CanSideTask { get; set; }

    /// <summary>The footer's Side task button: not in Compact, where the key legend lists B and the footer has no room.</summary>
    public bool ShowsSideTaskButton => CanSideTask && !IsCompact;

    /// <summary>The row ↑↓ is on (menu-style navigation); Enter picks it.</summary>
    [ObservableProperty]
    public partial int SelectedIndex { get; set; } = -1;

    public ObservableCollection<string> Comebacks { get; } = [];

    public ObservableCollection<string> NextUpLines { get; } = [];

    public ObservableCollection<OverlayBatteryRow> Devices { get; } = [];

    public ObservableCollection<OverlayBatteryRow> PanelBatteries => Devices;

    /// <summary>The open tasks, numbered (the running session's, or today's draft between sessions).</summary>
    public IReadOnlyList<TaskRowViewModel> Tasks => Model.Checklist?.OpenTasks ?? [];

    /// <summary>Reads everything again (the model's refresh calls it while the panel is open).</summary>
    public void Refresh()
    {
        SessionState state = _sessions.State;
        InSession = state.IsActive;
        HeaderSub = Model.IsSession ? $"{(state.CountdownFrozen ? "paused" : "left")} · {Model.EndsText}" : Model.Line2;
        ShowsExtend = state.CanExtendFocus;
        PauseText = state.IsAway ? "Resume" : "Pause";
        TasksHeader = state.IsActive ? "This session" : "Start focus on";
        SwitchHint = state.IsActive ? "1–3 to switch" : "1–3 to start";
        // During a break too: lunch cuts the break short.
        ShowsLunch = (!state.IsActive || state.Phase is SessionPhase.ShortBreak or SessionPhase.LongBreak) && Model.Actions.CanStartLunch();
        RefreshLunchRow();
        ShowsReady = Model.PlanFirst is { InStage: true };
        GoalText = Model.GoalText;
        OnPropertyChanged(nameof(HasQr));
        OnPropertyChanged(nameof(CanDisable));
        OnPropertyChanged(nameof(DisableLocked));
        OnPropertyChanged(nameof(DisableTip));
        OnPropertyChanged(nameof(HideTip));
        CanSideTask = state.IsEnforcing && !state.IsPlanning && !state.IsAway;
        OnPropertyChanged(nameof(ShowsSideTaskButton));
        OnPropertyChanged(nameof(Tasks));
        Fill(Comebacks, Model.Actions.ComebackTitles());
        Fill(NextUpLines, Model.Actions.NextUp().Select(row => $"{row.Time}\t{row.Text}").ToList());
        if (IsLarge)
        {
            Devices.Clear();
            foreach (OverlayBatteryRow row in Model.PanelBatteries())
            {
                Devices.Add(row);
            }
        }
    }

    /// <summary>Reads the lunch row again: shown only while lunch runs, its time left, and its question (closed with the row when lunch ends).</summary>
    public void RefreshLunchRow()
    {
        ShowsLunchRow = Model.Actions.LunchRunning();
        LunchLine = Model.Actions.LunchLine();
        LunchPrompt = Model.Actions.LunchPrompt();
        if (!ReferenceEquals(_watchedPrompt, LunchPrompt))
        {
            if (_watchedPrompt is not null)
            {
                _watchedPrompt.Closed -= OnLunchPromptClosed;
            }

            _watchedPrompt = LunchPrompt;
            if (_watchedPrompt is not null)
            {
                _watchedPrompt.Closed += OnLunchPromptClosed;
            }
        }

        if (!ShowsLunchRow && LunchPrompt is { IsOpen: true })
        {
            LunchPrompt.Close();
        }
    }

    // Answered or dismissed: read the row again (Count or Discard ended lunch under it).
    private void OnLunchPromptClosed(object? sender, EventArgs e) => RefreshLunchRow();

    private static void Fill(ObservableCollection<string> target, IReadOnlyList<string> lines)
    {
        if (target.SequenceEqual(lines))
        {
            return;
        }

        target.Clear();
        foreach (string line in lines)
        {
            target.Add(line);
        }
    }

    /// <summary>Ctrl+Alt with + or − while the panel is open.</summary>
    public void Resize(int step)
    {
        int next = Math.Clamp((int)Size + step, 0, 2);
        if (next == (int)Size)
        {
            return;
        }

        Size = (PanelSize)next;
        Refresh();
        _ = _settings.SaveLookAsync(settings => settings with { PanelSize = (PanelSize)next });
    }

    /// <summary>Raised when the panel should close (Hide, Open app, the gear).</summary>
    public event EventHandler? CloseRequested;

    [RelayCommand]
    private void Ready()
    {
        Model.PlanFirst?.Ready();
    }

    [RelayCommand]
    private void ShowQr()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
        Model.Actions.ShowSyncQr?.Invoke();
    }

    [RelayCommand]
    private void SideTask()
    {
        Model.StepAway?.AskSideTaskLength();
    }

    [RelayCommand]
    private void Hide()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
        Model.HideRequested?.Invoke();
    }

    [RelayCommand]
    private void Disable()
    {
        if (!CanDisable)
        {
            return;
        }

        CloseRequested?.Invoke(this, EventArgs.Empty);
        Model.DisableRequested?.Invoke();
    }

    [RelayCommand]
    private void OpenApp()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
        Model.Actions.OpenApp();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
        Model.Actions.OpenSettings();
    }

    [RelayCommand]
    private void Park()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
        Model.Actions.ParkThought();
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        await Model.Actions.StartDefault();
    }

    /// <summary>The lunch row's ✕: asks "End lunch?" (Count it, Discard it, Keep going) under the row.</summary>
    [RelayCommand]
    private void AskEndLunch()
    {
        if (LunchPrompt is { IsOpen: true })
        {
            LunchPrompt.Close();
        }
        else if (Model.Actions.LunchRunning())
        {
            // Only while lunch runs: a stale ✕ (lunch just ended) never opens an empty question.
            LunchPrompt?.Open();
        }
        else
        {
            RefreshLunchRow();
        }
    }

    [RelayCommand]
    private async Task StartLunchAsync()
    {
        if (Model.Actions.CanStartLunch())
        {
            await Model.Actions.StartLunch();
        }
    }

    /// <summary>A task's number: switches the current task in a session; between sessions it makes that task first and starts.</summary>
    [RelayCommand]
    private async Task PickAsync(int number)
    {
        if (number < 1 || number > Tasks.Count)
        {
            return;
        }

        TaskRowViewModel row = Tasks[number - 1];
        row.PickCommand.Execute(null);
        if (!_sessions.State.IsActive)
        {
            await Model.Actions.StartDefault();
        }
    }

    /// <summary>
    /// One key of the panel (a letter, a digit, an arrow, + or −), from a focused panel or from a Ctrl+Alt+key shortcut while it peeks.
    /// Returns true when the panel used it.
    /// </summary>
    public bool HandleKey(string key)
    {
        switch (key.ToUpperInvariant())
        {
            case "D" when Model.ShowsDone:
                Model.DoneCommand.Execute(null);
                return true;
            case "P" when Model.ShowsPause:
                Model.PauseCommand.Execute(null);
                return true;
            case "T" when ShowsExtend:
                Model.ExtendFocusCommand.Execute(null);
                return true;
            case "B" when CanSideTask:
                SideTaskCommand.Execute(null);
                return true;
            case "H" when HideShortcutOn:
                CloseRequested?.Invoke(this, EventArgs.Empty);
                Model.ToggleHiddenRequested?.Invoke();
                return true;
            case "H":
                HideCommand.Execute(null);
                return true;
            case "O":
                OpenAppCommand.Execute(null);
                return true;
            case "N":
                ParkCommand.Execute(null);
                return true;
            case "M" when Model.Noise is not null:
                Model.Noise.PlayPauseCommand.Execute(null);
                return true;
            case "F" when !InSession:
                StartCommand.Execute(null);
                return true;
            case "L" when ShowsLunch:
                StartLunchCommand.Execute(null);
                return true;
            case "1" or "2" or "3":
                PickCommand.Execute(int.Parse(key));
                return true;
            case "UP":
                SelectedIndex = Math.Max(0, SelectedIndex - 1);
                return true;
            case "DOWN":
                SelectedIndex = Math.Min(Tasks.Count - 1, SelectedIndex + 1);
                return true;
            case "ENTER" when SelectedIndex >= 0:
                PickCommand.Execute(SelectedIndex + 1);
                return true;
            case "+" or "ADD" or "OEMPLUS":
                Resize(1);
                return true;
            case "-" or "SUBTRACT" or "OEMMINUS":
                Resize(-1);
                return true;
            default:
                return false;
        }
    }
}
