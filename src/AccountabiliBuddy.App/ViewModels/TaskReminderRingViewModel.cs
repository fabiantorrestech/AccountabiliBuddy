using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The minimal task reminder window (ADR 76): the front reminder, "1 of 2", Work on it now, Snooze 10 min, Pick a time… (Snooze until…)
/// and Dismiss (which waits out dismiss protection). A stand-in until the reminder cards of the redesign replace it. Holds a task's title: shown, never logged.
/// </summary>
public sealed partial class TaskReminderRingViewModel : ObservableObject
{
    private readonly Action _onDismiss;
    private readonly Action _onSnooze;
    private readonly Action _onStart;
    private readonly Action? _onPick;

    public TaskReminderRingViewModel(Action onDismiss, Action onSnooze, Action onStart, string snoozeText, Action? onPick = null)
    {
        _onDismiss = onDismiss;
        _onSnooze = onSnooze;
        _onStart = onStart;
        _onPick = onPick;
        SnoozeText = snoozeText;
    }

    /// <summary>"Pick a time…" (Snooze until…) is offered.</summary>
    public bool CanPick => _onPick is not null;

    public string SnoozeText { get; }

    [ObservableProperty]
    private string _heading = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _counter = string.Empty;

    [ObservableProperty]
    private string _note = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DismissCommand))]
    private bool _canDismiss;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SnoozeCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(PickCommand))]
    private bool _canAct = true;

    [ObservableProperty]
    private string _dismissText = "Dismiss";

    [RelayCommand(CanExecute = nameof(CanDismiss))]
    private void Dismiss() => _onDismiss();

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void Snooze() => _onSnooze();

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void Start() => _onStart();

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void Pick() => _onPick?.Invoke();
}
