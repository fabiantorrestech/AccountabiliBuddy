using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.Services.Timers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A timer on Today's Timers card: time left, Pause/Resume, +1 min, Reset, ✎ (name and length) and delete.</summary>
public sealed partial class TimerRowViewModel : ObservableObject
{
    private readonly TimersService _timers;

    public TimerRowViewModel(TimersService timers, TimerItem item)
    {
        _timers = timers;
        Item = item;
        Name = item.Name;
        TimeText = string.Empty;
        EditName = string.Empty;
        Refresh(item);
    }

    public TimerItem Item { get; private set; }

    public Guid Id => Item.Id;

    [ObservableProperty]
    public partial string Name { get; set; }

    /// <summary>"4:12 left · ends 3:05 PM", "Paused · 4:12 left" or "Done · 10 min".</summary>
    [ObservableProperty]
    public partial string TimeText { get; set; }

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial MaterialIconKind PauseIcon { get; set; }

    [ObservableProperty]
    public partial string PauseTip { get; set; } = "Pause";

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    [ObservableProperty]
    public partial string EditName { get; set; }

    [ObservableProperty]
    public partial decimal? EditMinutes { get; set; }

    /// <summary>The timer as saved now; the time left is worked out again each second.</summary>
    public void Refresh(TimerItem item)
    {
        Item = item;
        Name = item.Name;
        IsRunning = item.State == TimerState.Running;
        PauseIcon = IsRunning ? MaterialIconKind.Pause : MaterialIconKind.Play;
        PauseTip = item.State switch
        {
            TimerState.Running => "Pause",
            TimerState.Paused => "Resume",
            _ => "Start it again",
        };
        Tick();
    }

    public void Tick()
    {
        DateTimeOffset now = _timers.Now;
        string left = SessionStatusFormatter.FormatDuration(Item.LeftAt(now));
        TimeText = Item.State switch
        {
            TimerState.Running when Item.EndsAt is DateTimeOffset end =>
                $"{left} left · ends {TimeZoneInfo.ConvertTime(end, TimeZoneInfo.Local):h:mm tt}",
            TimerState.Paused => $"Paused · {left} left",
            _ => $"Done · {(int)Math.Round(Item.Length.TotalMinutes)} min",
        };
    }

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
    private void Reset()
    {
        _ = _timers.ResetAsync(Id);
    }

    [RelayCommand]
    private void Edit()
    {
        EditName = Item.Name;
        EditMinutes = (decimal)Math.Max(1, Math.Round(Item.Length.TotalMinutes));
        IsEditing = true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        IsEditing = false;
        if (EditMinutes is decimal minutes && minutes > 0)
        {
            await _timers.EditTimerAsync(Id, EditName, TimeSpan.FromMinutes((double)minutes));
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        IsEditing = false;
    }

    [RelayCommand]
    private void Delete()
    {
        _ = _timers.DeleteTimerAsync(Id);
    }
}
