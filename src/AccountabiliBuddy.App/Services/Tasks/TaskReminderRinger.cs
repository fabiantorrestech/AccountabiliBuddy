using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.App.Views;
using AccountabiliBuddy.Core.Tasks;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// Rings the task reminders that are due (ADR 76): one window for the whole queue ("1 of 2"), the bell repeating while it shows, rising
/// over 30 s when set, and the output raised when "Ring even when the PC is muted" is on (always put back, see
/// <see cref="MasterVolumeGuard"/>). The window follows <see cref="TaskReminderService.Pending"/>: a reminder handled on another device
/// leaves it, and it closes when none is left. Each reminder stops by itself after the ring time (Can't miss rings longest) and then
/// isn't rung again. Dismiss waits out dismiss protection; Snooze and Work on it now work at once.
/// During a focus session (spec: reminders keep ringing; the session is never loosened by them) a normal reminder shows without taking
/// the focus and its bell sounds once; Can't miss rings as it always does. Nothing here ends, pauses or changes a session.
/// This window is a stand-in: the redesign's reminder cards replace it. Use from the UI thread only.
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
    private readonly Dictionary<string, DateTimeOffset> _shownAt = [];
    private TaskReminderWindow? _window;
    private TaskReminderRingViewModel? _viewModel;
    private TaskReminderEntry? _front;
    private DateTimeOffset _lastSound;
    private bool _soundedInSession;

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

        DateTimeOffset now = _reminders.Now;
        foreach (TaskReminderEntry entry in pending.Where(entry => !_shownAt.ContainsKey(entry.Key)))
        {
            _shownAt[entry.Key] = now;
        }

        foreach (string key in _shownAt.Keys.Where(key => pending.All(entry => entry.Key != key)).ToList())
        {
            _shownAt.Remove(key);
        }

        if (_window is null)
        {
            Begin(pending[0], now);
        }
        else
        {
            Show(pending);
        }
    }

    private void Begin(TaskReminderEntry first, DateTimeOffset now)
    {
        bool inSession = _sessionRunning();
        _soundedInSession = false;
        _lastSound = DateTimeOffset.MinValue;
        if (_settings.Current.RingEvenWhenMuted)
        {
            _guard.RaiseFor(first.CantMiss ? 100 : _settings.Current.Volume);
        }

        _glow();
        _viewModel = new TaskReminderRingViewModel(
            onDismiss: () => _ = ActAsync(entry => _reminders.DismissAsync(entry)),
            onSnooze: () => _ = ActAsync(entry => _reminders.SnoozeAsync(entry, TaskSnooze.FromNow(_reminders.Now, QuickSnoozeMinutes))),
            onStart: () => _ = ActAsync(entry => _reminders.StartAsync(entry)),
            snoozeText: $"Snooze {QuickSnoozeMinutes} min");
        _window = new TaskReminderWindow { DataContext = _viewModel, ShowActivated = !inSession || first.CantMiss };
        _window.Closed += (_, _) => _window = null;
        Show(_reminders.Pending);
        _window.Show();
        if (!inSession || first.CantMiss)
        {
            _window.Activate();
        }

        _tick.Start();
        OnTick();
    }

    // The front reminder's words and buttons; the queue behind it counts in "1 of N".
    private void Show(IReadOnlyList<TaskReminderEntry> pending)
    {
        if (_viewModel is null)
        {
            return;
        }

        TaskReminderEntry front = pending[0];
        if (_front?.Key != front.Key)
        {
            // A new front reminder: its ring starts over from its own first showing (protection, volume ramp).
            _front = front;
            _lastSound = DateTimeOffset.MinValue;
        }

        _viewModel.Heading = (front.CantMiss ? "Can't miss · " : "Reminder · ") + TaskReminderText.Rule(front, _reminders.Zone);
        _viewModel.Title = _reminders.TitleOf(front.TaskId) ?? string.Empty;
        _viewModel.Counter = pending.Count > 1 ? $"1 of {pending.Count}" : string.Empty;
        _viewModel.Note = front.SnoozeCount >= 3 ? $"Snoozed {front.SnoozeCount} times. Put it in Queued?" : string.Empty;
        UpdateDismiss();
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

        // A reminder that rang its whole ring time stops by itself and isn't rung again.
        TaskReminderEntry? expired = pending.FirstOrDefault(entry => now - ShownAt(entry, now) >= policy.StopAfter(entry.CantMiss));
        if (expired is not null)
        {
            _reminders.RingTimedOut(expired);
            return;
        }

        TaskReminderEntry front = pending[0];
        UpdateDismiss();
        PlaySoundIfDue(front, now, policy);
    }

    private void PlaySoundIfDue(TaskReminderEntry front, DateTimeOffset now, TaskReminderRingPolicy policy)
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
        _cues.Play(SoundChoice.BellId, policy.VolumeAt(now - ShownAt(front, now), front.CantMiss));
    }

    private void UpdateDismiss()
    {
        if (_viewModel is null || _reminders.Pending.Count == 0)
        {
            return;
        }

        TaskReminderEntry front = _reminders.Pending[0];
        DateTimeOffset now = _reminders.Now;
        TimeSpan left = Policy.DismissWait() - (now - ShownAt(front, now));
        bool waiting = left > TimeSpan.Zero;
        _viewModel.CanDismiss = !waiting;
        _viewModel.DismissText = waiting ? $"Dismiss in {Math.Ceiling(left.TotalSeconds):0} s" : "Dismiss";
    }

    private DateTimeOffset ShownAt(TaskReminderEntry entry, DateTimeOffset fallback)
    {
        return _shownAt.TryGetValue(entry.Key, out DateTimeOffset at) ? at : fallback;
    }

    private async Task ActAsync(Func<TaskReminderEntry, Task> act)
    {
        if (_reminders.Pending.Count > 0)
        {
            await act(_reminders.Pending[0]);
        }
    }

    private void Stop()
    {
        _tick.Stop();
        _front = null;
        _shownAt.Clear();
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
