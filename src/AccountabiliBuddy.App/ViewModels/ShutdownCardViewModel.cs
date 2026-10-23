using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The shutdown card (redesign phase 5, design J): at the end of the day's last work part, open come-backs to tick,
/// today's unfinished tasks to carry or drop, and tomorrow's first task. Done applies them; Skip closes it unchanged.
/// </summary>
public sealed partial class ShutdownCardViewModel : ObservableObject
{
    private readonly Func<ShutdownCardViewModel, Task> _apply;
    private readonly Func<Task> _close;

    /// <param name="apply">Writes the choices and closes the card (set by the service).</param>
    /// <param name="close">Closes it without changes.</param>
    public ShutdownCardViewModel(
        IReadOnlyList<ShutdownComebackRow> comebacks, IReadOnlyList<ShutdownTaskRow> unfinished, Func<ShutdownCardViewModel, Task> apply, Func<Task> close)
    {
        Comebacks = comebacks;
        Unfinished = unfinished;
        _apply = apply;
        _close = close;
        TomorrowTask = string.Empty;
    }

    public IReadOnlyList<ShutdownComebackRow> Comebacks { get; }

    public IReadOnlyList<ShutdownTaskRow> Unfinished { get; }

    public bool HasComebacks => Comebacks.Count > 0;

    public bool HasUnfinished => Unfinished.Count > 0;

    /// <summary>Tomorrow's first task: it waits in Today's checklist, first.</summary>
    [ObservableProperty]
    public partial string TomorrowTask { get; set; }

    [RelayCommand]
    private Task DoneAsync()
    {
        return _apply(this);
    }

    [RelayCommand]
    private Task SkipAsync()
    {
        return _close();
    }
}
