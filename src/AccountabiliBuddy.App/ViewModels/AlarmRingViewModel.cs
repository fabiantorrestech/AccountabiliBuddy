using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// A ringing alarm's window (the user's alarms, and this PC's wake alarm, ADR 35): Stop, and a split "Snooze 9 min" button
/// (1, 5, 10, 15, 30 or a custom 1-180) that also says when it rings again (see <see cref="MinutesChoiceViewModel"/>).
/// </summary>
public sealed partial class AlarmRingViewModel : MinutesChoiceViewModel
{
    public const int DefaultSnoozeMinutes = 9;
    private static readonly int[] PresetMinutes = [1, 5, 10, 15, 30];

    private readonly Action _onStop;

    public AlarmRingViewModel(string title, string text, string stopText, Func<DateTime> localNow, Action onStop, Action<int> onSnooze)
        : base(DefaultSnoozeMinutes, PresetMinutes, "{0} min", "Rings again at", MaterialIconKind.AlarmSnooze, localNow, onSnooze)
    {
        Title = title;
        Text = text;
        StopText = stopText;
        _onStop = onStop;
    }

    public string Title { get; }

    public string Text { get; }

    public string StopText { get; }

    /// <summary>The main part of the button: "Snooze 9 min" (the menu entries are just "5 min" and so on).</summary>
    public override string MainText => $"Snooze {DefaultText}";

    [RelayCommand]
    private void Stop() => _onStop();
}
