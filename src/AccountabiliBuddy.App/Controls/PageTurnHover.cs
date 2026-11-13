using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// A task or a column dragged over the page bar's ‹ › turns the page after <see cref="Delay"/> (round 2 PG1, owner 2026-11-14), and again
/// every <see cref="Delay"/> while it stays there, so a drag can reach any column. A button opts in with <see cref="StepProperty"/>
/// (-1 or 1); the drags (a row's handle, a column's grip) report what is under the pointer through <see cref="Update"/> and call
/// <see cref="Cancel"/> when they end. One drag at a time, so the state is static. The timer is replaceable for the checks.
/// </summary>
public static class PageTurnHover
{
    public static readonly AttachedProperty<int> StepProperty =
        AvaloniaProperty.RegisterAttached<Button, int>("Step", typeof(PageTurnHover));

    private static Button? _hot;
    private static IDisposable? _timer;

    /// <summary>How long a held drag waits before it turns the page (and between turns).</summary>
    public static TimeSpan Delay { get; } = TimeSpan.FromMilliseconds(600);

    /// <summary>Starts a repeating timer that calls the action every interval; replaced by the checks with a hand-driven one.</summary>
    public static Func<TimeSpan, Action, IDisposable> Schedule { get; set; } = DefaultSchedule;

    public static int GetStep(Button button) => button.GetValue(StepProperty);

    public static void SetStep(Button button, int value) => button.SetValue(StepProperty, value);

    /// <summary>The button a drag is held over now, if any.</summary>
    public static Button? Hot => _hot;

    /// <summary>The drag is over <paramref name="hit"/> (the control under the pointer; null: nothing): a page button starts or keeps the wait.</summary>
    public static void Update(Visual? hit)
    {
        Button? button = hit?.GetSelfAndVisualAncestors().OfType<Button>().FirstOrDefault(candidate => GetStep(candidate) != 0);
        if (ReferenceEquals(button, _hot))
        {
            return;
        }

        Cancel();
        if (button is not { IsEnabled: true } || button.Command is { } command && !command.CanExecute(null))
        {
            return;
        }

        _hot = button;
        button.Classes.Add("dragHot");
        _timer = Schedule(Delay, Turn);
    }

    /// <summary>The drag ended or left the buttons.</summary>
    public static void Cancel()
    {
        _timer?.Dispose();
        _timer = null;
        _hot?.Classes.Remove("dragHot");
        _hot = null;
    }

    private static void Turn()
    {
        if (_hot is not { Command: { } command } || !command.CanExecute(null))
        {
            // The end of the pages: nothing more to turn to.
            Cancel();
            return;
        }

        command.Execute(null);
    }

    private static IDisposable DefaultSchedule(TimeSpan interval, Action tick)
    {
        var timer = new DispatcherTimer { Interval = interval };
        timer.Tick += (_, _) => tick();
        timer.Start();
        return new TimerHandle(timer);
    }

    private sealed class TimerHandle(DispatcherTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }
}
