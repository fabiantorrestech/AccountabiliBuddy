using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.App.Views;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Rings a bedtime reminder on this PC (ADR 38): the alarm window ("Bedtime · n/N", Dismiss and Snooze 5 min), the bell
/// every 10 seconds and the glow, for at most 10 minutes. Stops when it's dismissed or snoozed on another device. Use
/// from the UI thread only.
/// </summary>
public sealed class BedtimeAlarmRinger
{
    private static readonly TimeSpan MaxRing = TimeSpan.FromMinutes(10);

    private readonly BedtimeReminderService _reminders;
    private readonly SessionCueService _cues;
    private readonly Action _glow;
    private readonly DispatcherTimer _sound = new() { Interval = TimeSpan.FromSeconds(10) };
    private Window? _window;
    private int? _ringing;
    private DateTime _startedAt;

    public BedtimeAlarmRinger(BedtimeReminderService reminders, SessionCueService cues, Action glow)
    {
        _reminders = reminders;
        _cues = cues;
        _glow = glow;
        _sound.Tick += (_, _) =>
        {
            if (DateTime.UtcNow - _startedAt > MaxRing)
            {
                Stop();
                return;
            }

            _cues.Play(SoundChoice.BellId, 100);
        };
        _reminders.AlarmDue += (_, alarm) => Ring(alarm);
        _reminders.StopRinging += (_, number) =>
        {
            if (_ringing == number)
            {
                Stop();
            }
        };
    }

    private void Ring(BedtimeAlarm alarm)
    {
        Stop();
        _ringing = alarm.Number;
        _startedAt = DateTime.UtcNow;
        _glow();
        _cues.Play(SoundChoice.BellId, 100);
        _sound.Start();
        var viewModel = new PcAlarmViewModel(alarm.Label, alarm.Text,
            onStop: () => { _reminders.Dismiss(alarm.Number); Stop(); },
            onSnooze: () => { _reminders.SnoozeIt(alarm.Number); Stop(); });
        _window = new PcAlarmWindow { DataContext = viewModel };
        _window.Closed += (_, _) => _window = null;
        _window.Show();
        _window.Activate();
    }

    private void Stop()
    {
        _sound.Stop();
        _ringing = null;
        Window? window = _window;
        _window = null;
        window?.Close();
    }
}
