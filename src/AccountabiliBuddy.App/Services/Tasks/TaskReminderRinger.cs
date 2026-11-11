using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.App.Views;
using AccountabiliBuddy.Core.Tasks;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// Rings the task reminders that are due (ADR 76): one window for the whole queue ("1 of 2"), the bell repeating while it shows, rising
/// over 30 s when set, and the output raised when "Ring even when the PC is muted" is on (always put back, see
/// <see cref="MasterVolumeGuard"/>). The window follows <see cref="TaskReminderService.Pending"/>: a reminder handled on another device
/// leaves it, and it closes when none is left; closed by Alt+F4 or the taskbar while reminders wait, it comes back. Only the front
/// reminder rings: its dismiss wait, volume ramp and ring time all count from the moment it reached the front, and when its ring time
/// is used up it is not rung again. Dismiss waits out dismiss protection; Snooze and Work on it now work at once; while an action is
/// written the buttons are off.
/// During a focus session the service holds normal reminders (setting "During a focus session": wait until it ends, or remind quietly).
/// A quiet one shows without taking the focus and its bell sounds once; Can't miss rings as it always does. Nothing here ends, pauses or
/// changes a session. This window is a stand-in: the redesign's reminder cards replace it. Use from the UI thread only.
/// </summary>
public sealed class TaskReminderRinger : IDisposable
{
    private const int QuickSnoozeMinutes = 10;

    private readonly TaskReminderService _reminders;
    private readonly TaskReminderSettingsService _settings;
    private readonly SessionCueService _cues;
    private readonly MasterVolumeGuard _guard;
    private readonly Func<bool> _sessionRunning;
    private readonly Action _glow;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private TaskReminderWindow? _window;
    private TaskReminderRingViewModel? _viewModel;
    private TaskReminderEntry? _front;
    private DateTimeOffset _frontSince;
    private DateTimeOffset _lastSound;
    private bool _soundedInSession;
    private bool _busy;
    private bool _disposed;

    public TaskReminderRinger(
        TaskReminderService reminders, TaskReminderSettingsService settings, SessionCueService cues, MasterVolumeGuard guard,
        Func<bool> sessionRunning, Action glow)
    {
        _reminders = reminders;
        _settings = settings;
        _cues = cues;
        _guard = guard;
        _sessionRunning = sessionRunning;
        _glow = glow;
        _tick.Tick += (_, _) => OnTick();
        _reminders.PendingChanged += (_, _) => OnPendingChanged();
    }

    public bool IsRinging => _window is not null;

    public void Dispose()
    {
        _disposed = true;
        _tick.Stop();
        CloseWindow();
        _guard.Restore();
    }

    private TaskReminderRingPolicy Policy => _settings.Current.Policy;

    private void OnPendingChanged()
    {
        IReadOnlyList<TaskReminderEntry> pending = _reminders.Pending;
        if (pending.Count == 0)
        {
            Stop();
            return;
        }

        // Everything about a ring (dismiss wait, volume ramp, ring time) counts from the moment the reminder reaches the front;
        // one waiting behind it has not been seen yet.
        TaskReminderEntry front = pending[0];
        if (_front?.Key != front.Key)
        {
            _front = front;
            _frontSince = _reminders.Now;
            _lastSound = DateTimeOffset.MinValue;
            if (_window is not null && _settings.Current.RingEvenWhenMuted && front.CantMiss)
            {
                // A Can't miss that reaches the front later needs the louder output too.
                _guard.RaiseFor(100);
            }
        }

        if (_window is null)
        {
            Begin(front);
        }
        else
        {
            Show(pending);
        }
    }

    private void Begin(TaskReminderEntry front)
    {
        bool inSession = _sessionRunning();
        _soundedInSession = false;
        if (_settings.Current.RingEvenWhenMuted)
        {
            _guard.RaiseFor(front.CantMiss ? 100 : _settings.Current.Volume);
        }

        _glow();
        _viewModel = new TaskReminderRingViewModel(
            onDismiss: () => _ = ActAsync(entry => _reminders.DismissAsync(entry)),
            onSnooze: () => _ = ActAsync(entry => _reminders.SnoozeAsync(entry, TaskSnooze.FromNow(_reminders.Now, QuickSnoozeMinutes))),
            onStart: () => _ = ActAsync(entry => _reminders.StartAsync(entry)),
            snoozeText: $"Snooze {QuickSnoozeMinutes} min");
        var window = new TaskReminderWindow { DataContext = _viewModel, ShowActivated = !inSession || front.CantMiss };
        _window = window;
        window.Closed += (_, _) =>
        {
            // Closed by Alt+F4 or the taskbar (not by us): reminders still wait, so the window comes back.
            if (ReferenceEquals(_window, window))
            {
                _window = null;
                _viewModel = null;
                if (!_disposed && _reminders.Pending.Count > 0)
                {
                    Dispatcher.UIThread.Post(Reopen);
                }
            }
        };
        Show(_reminders.Pending);
        window.Show();
        if (!inSession || front.CantMiss)
        {
            window.Activate();
        }

        _tick.Start();
        OnTick();
    }

    private void Reopen()
    {
        if (_window is null && !_disposed && _reminders.Pending.Count > 0)
        {
            Begin(_reminders.Pending[0]);
        }
    }

    // The front reminder's words and buttons; the queue behind it counts in "1 of N".
    private void Show(IReadOnlyList<TaskReminderEntry> pending)
    {
        if (_viewModel is null)
        {
            return;
        }

        TaskReminderEntry front = pending[0];
        _viewModel.Heading = (front.CantMiss ? "Can't miss · " : "Reminder · ") + TaskReminderText.Rule(front, _reminders.Zone);
        _viewModel.Title = _reminders.TitleOf(front.TaskId) ?? string.Empty;
        _viewModel.Counter = pending.Count > 1 ? $"1 of {pending.Count}" : string.Empty;
        _viewModel.Note = front.SnoozeCount >= 3 ? $"Snoozed {front.SnoozeCount} times. Put it in Queued?" : string.Empty;
        UpdateButtons();
    }

    private void OnTick()
    {
        IReadOnlyList<TaskReminderEntry> pending = _reminders.Pending;
        if (pending.Count == 0)
        {
            Stop();
            return;
        }

        DateTimeOffset now = _reminders.Now;
        TaskReminderRingPolicy policy = Policy;
        TaskReminderEntry front = pending[0];
        TimeSpan shown = now - _frontSince;

        // Only the front one rings, so only its ring time runs; when it is used up it is not rung again and the next comes to the front.
        if (policy.Expired(shown, front.CantMiss))
        {
            _reminders.RingTimedOut(front);
            return;
        }

        UpdateButtons();
        PlaySoundIfDue(front, now, shown, policy);
    }

    private void PlaySoundIfDue(TaskReminderEntry front, DateTimeOffset now, TimeSpan shown, TaskReminderRingPolicy policy)
    {
        bool quiet = _sessionRunning() && !front.CantMiss;
        if (quiet && _soundedInSession)
        {
            return;
        }

        if (now - _lastSound < policy.RepeatEvery(front.CantMiss))
        {
            return;
        }

        _lastSound = now;
        _soundedInSession |= quiet;
        _cues.Play(SoundChoice.BellId, policy.VolumeAt(shown, front.CantMiss));
    }

    private void UpdateButtons()
    {
        if (_viewModel is null || _reminders.Pending.Count == 0)
        {
            return;
        }

        TimeSpan left = Policy.DismissLeft(_reminders.Now - _frontSince);
        bool waiting = left > TimeSpan.Zero;
        _viewModel.CanDismiss = !waiting && !_busy;
        _viewModel.CanAct = !_busy;
        _viewModel.DismissText = waiting ? $"Dismiss in {Math.Ceiling(left.TotalSeconds):0} s" : "Dismiss";
    }

    // One action at a time: while it is written, the buttons are off (a second click would snooze twice or act on the next reminder).
    private async Task ActAsync(Func<TaskReminderEntry, Task> act)
    {
        if (_busy || _reminders.Pending.Count == 0)
        {
            return;
        }

        _busy = true;
        UpdateButtons();
        try
        {
            await act(_reminders.Pending[0]);
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    private void Stop()
    {
        _tick.Stop();
        _front = null;
        CloseWindow();
        _guard.Restore();
    }

    private void CloseWindow()
    {
        TaskReminderWindow? window = _window;
        _window = null;
        _viewModel = null;
        window?.Close();
    }
}
