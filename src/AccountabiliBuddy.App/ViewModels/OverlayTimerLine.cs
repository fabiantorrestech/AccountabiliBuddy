using AccountabiliBuddy.App.Services.Timers;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One timer or alarm on the on-screen window: its name and time left (an alarm: when it rings, 12-hour) over a thin
/// bar; with Ctrl+Alt held, Pause/Resume, +1 min and ✕ (stop, or dismiss once it's up).
/// </summary>
public sealed partial class OverlayTimerLine : ObservableObject
{
    private readonly TimersService _timers;

    public OverlayTimerLine(TimersService timers, Guid id, bool isAlarm)
    {
        _timers = timers;
        Id = id;
        IsAlarm = isAlarm;
        Icon = isAlarm ? MaterialIconKind.Alarm : MaterialIconKind.Timer;
        Name = string.Empty;
        TimeText = string.Empty;
        BarBrush = Brushes.Transparent;
    }

    public Guid Id { get; }

    public bool IsAlarm { get; }

    /// <summary>Timers have buttons and a bar; alarms only a line.</summary>
    public bool IsTimer => !IsAlarm;

    public MaterialIconKind Icon { get; }

    [ObservableProperty]
    public partial string Name { get; set; }

    /// <summary>"4:12 left", "paused · 4:12", "up", or for an alarm "7:30 AM".</summary>
    [ObservableProperty]
    public partial string TimeText { get; set; }

    /// <summary>Filled part of the bar, 0 to <see cref="OverlayTimersViewModel.BarLength"/>.</summary>
    [ObservableProperty]
    public partial double BarWidth { get; set; }

    [ObservableProperty]
    public partial IBrush BarBrush { get; set; }

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    /// <summary>Pause while running, else Play.</summary>
    [ObservableProperty]
    public partial MaterialIconKind PauseIcon { get; set; }

    /// <summary>"Pause" or "Resume".</summary>
    [ObservableProperty]
    public partial string PauseTip { get; set; } = "Pause";

    [ObservableProperty]
    public partial bool IsUp { get; set; }

    [ObservableProperty]
    public partial string StopTip { get; set; } = "Stop this timer";

    [RelayCommand]
    private void PauseResume()
    {
        _ = IsRunning ? _timers.PauseAsync(Id) : _timers.ResumeAsync(Id);
    }

    [RelayCommand]
    private void AddMinute()
    {
        _ = _timers.AddMinuteAsync(Id);
    }

    [RelayCommand]
    private void Stop()
    {
        _ = IsUp ? _timers.DismissTimerAsync(Id) : _timers.StopAsync(Id);
    }
}
