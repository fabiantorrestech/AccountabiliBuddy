using System.Globalization;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>This PC's alarm window (ADR 35): the wake time, Stop and Snooze 9 min.</summary>
public sealed partial class PcAlarmViewModel
{
    private readonly Action _onStop;
    private readonly Action _onSnooze;

    public PcAlarmViewModel(DateTime wake, Action onStop, Action onSnooze)
    {
        _onStop = onStop;
        _onSnooze = onSnooze;
        Title = $"Good morning · {wake.ToString("h:mm tt", CultureInfo.InvariantCulture)}";
    }

    public string Title { get; }

    [RelayCommand]
    private void Stop() => _onStop();

    [RelayCommand]
    private void Snooze() => _onSnooze();
}
