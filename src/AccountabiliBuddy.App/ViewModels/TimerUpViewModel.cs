using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The "Timer · Tea is up" window: Dismiss, and a split "+5 min" button (+1, +5, +10, +15 or a custom 1-180) that also says
/// when the timer will go off again (see <see cref="MinutesChoiceViewModel"/>).
/// </summary>
public sealed partial class TimerUpViewModel : MinutesChoiceViewModel
{
    public const int DefaultExtendMinutes = 5;
    private static readonly int[] PresetMinutes = [1, 5, 10, 15];

    private readonly Action _onDismiss;

    public TimerUpViewModel(string title, string text, Func<DateTime> localNow, Action onDismiss, Action<int> onExtend)
        : base(DefaultExtendMinutes, PresetMinutes, "+{0} min", "Goes off at", MaterialIconKind.TimerPlusOutline, localNow, onExtend)
    {
        Title = title;
        Text = text;
        _onDismiss = onDismiss;
    }

    public string Title { get; }

    public string Text { get; }

    [RelayCommand]
    private void Dismiss() => _onDismiss();
}
