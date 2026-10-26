using System.Globalization;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>This PC's alarm window (ADR 35): the wake time, Stop and Snooze 9 min.</summary>
public sealed partial class PcAlarmViewModel
{
    private readonly Action _onStop;
    private readonly Action _onSnooze;

    public PcAlarmViewModel(DateTime wake, Action onStop, Action onSnooze)
        : this($"Good morning · {wake.ToString("h:mm tt", CultureInfo.InvariantCulture)}", "Your wake time. Snooze rings again in 9 minutes.", onStop, onSnooze, "Snooze 9 min", "Stop")
    {
    }

    /// <summary>Any alarm: a bedtime reminder (ADR 38) is "Bedtime · n/N" with Dismiss and Snooze 5 min.</summary>
    public PcAlarmViewModel(string title, string text, Action onStop, Action onSnooze, string snoozeText = "Snooze 5 min", string stopText = "Dismiss")
    {
        _onStop = onStop;
        _onSnooze = onSnooze;
        Title = title;
        Text = text;
        SnoozeText = snoozeText;
        StopText = stopText;
    }

    public string Title { get; }

    public string Text { get; }

    public string SnoozeText { get; }

    public string StopText { get; }

    [RelayCommand]
    private void Stop() => _onStop();

    [RelayCommand]
    private void Snooze() => _onSnooze();
}
