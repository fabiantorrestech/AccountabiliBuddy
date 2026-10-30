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

    public OverlayPanelViewModel(TaskbarOverlayViewModel model, OverlaySettingsService settings, SessionController sessions)
    {
        Model = model;
        _settings = settings;
        _sessions = sessions;
        HeaderSub = TasksHeader = SwitchHint = PauseText = string.Empty;
        Size = settings.Current.PanelSize;
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
        HeaderSub = Model.IsSession ? $"{(state.IsAway ? "paused" : "left")} · {Model.EndsText}" : Model.Line2;
        ShowsExtend = state.CanExtendFocus;
        PauseText = state.IsAway ? "Resume" : "Pause";
        TasksHeader = state.IsActive ? "This session" : "Start focus on";
        SwitchHint = state.IsActive ? "1–3 to switch" : "1–3 to start";
        ShowsLunch = !state.IsActive && Model.Actions.CanStartLunch();
        ShowsReady = Model.PlanFirst is { InStage: true };
        GoalText = Model.GoalText;
        OnPropertyChanged(nameof(HasQr));
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
