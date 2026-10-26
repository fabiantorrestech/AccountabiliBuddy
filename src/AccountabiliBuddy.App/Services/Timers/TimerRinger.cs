using System.Globalization;
using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.App.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Services.Timers;

/// <summary>
/// Rings a timer or alarm on this PC like a phase end: the on-screen window's glow and the chosen "time is up" chime
/// (Settings → Sounds when time is up; with "Off" the bell, so it's never silent), plus a small window ("Timer · Tea is up" with Dismiss and
/// +1 min; "Alarm · Wake" with Dismiss and Snooze 9 min). The chime comes again every 10 seconds while it rings, for at
/// most 2 minutes (a timer) or 10 (an alarm). Several can ring at once, one window each, each 24 px further down and
/// right than the last so none hides another. The window shows without
/// taking focus, so typing elsewhere isn't caught. Use from the UI thread only.
/// </summary>
public sealed class TimerRinger
{
    private static readonly TimeSpan TimerMaxRing = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan AlarmMaxRing = TimeSpan.FromMinutes(10);
    private const double StackOffset = 24;

    private readonly TimersService _timers;
    private readonly Action _playChime;
    private readonly Action _glow;
    private readonly DispatcherTimer _sound = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly Dictionary<Guid, Ringing> _ringing = [];

    /// <param name="playChime">Plays the chosen time-is-up sound.</param>
    /// <param name="glow">The on-screen window's white glow.</param>
    public TimerRinger(TimersService timers, Action playChime, Action glow)
    {
        _timers = timers;
        _playChime = playChime;
        _glow = glow;
        _sound.Tick += (_, _) => RepeatChime();
        _timers.TimerUp += (_, timer) => RingTimer(timer);
        _timers.AlarmUp += (_, alarm) => RingAlarm(alarm);
        // Handled elsewhere (Today, the on-screen window) or deleted: stop ringing it.
        _timers.Changed += (_, _) => StopSettled();
    }

    private void RingTimer(TimerItem timer)
    {
        var viewModel = new PcAlarmViewModel(
            $"Timer · {timer.Name} is up",
            $"Set for {DescribeLength(timer.Length)}. +1 min runs it a minute more.",
            onStop: () => _ = _timers.DismissTimerAsync(timer.Id),
            onSnooze: () => _ = _timers.AddMinuteAsync(timer.Id),
            snoozeText: "+1 min");
        Ring(timer.Id, viewModel, TimerMaxRing, isAlarm: false, onClosedByHand: () => _ = _timers.DismissTimerAsync(timer.Id));
    }

    private void RingAlarm(AlarmItem alarm)
    {
        string time = alarm.Time.ToString("h:mm tt", CultureInfo.InvariantCulture);
        var viewModel = new PcAlarmViewModel(
            $"Alarm · {alarm.Name}",
            $"{time}. Snooze rings again in 9 minutes.",
            onStop: () => { _ = _timers.DismissAlarmAsync(alarm.Id); Stop(alarm.Id); },
            onSnooze: () => { _ = _timers.SnoozeAlarmAsync(alarm.Id); Stop(alarm.Id); },
            snoozeText: "Snooze 9 min");
        Ring(alarm.Id, viewModel, AlarmMaxRing, isAlarm: true, onClosedByHand: () => _ = _timers.DismissAlarmAsync(alarm.Id));
    }

    private void Ring(Guid id, PcAlarmViewModel viewModel, TimeSpan maxRing, bool isAlarm, Action onClosedByHand)
    {
        Stop(id);
        _glow();
        _playChime();
        var window = new PcAlarmWindow { DataContext = viewModel, ShowActivated = false, Title = isAlarm ? "Alarm" : "Timer" };
        var ringing = new Ringing(window, DateTime.UtcNow, maxRing, isAlarm);
        // Each window already ringing moves this one 24 px down and right (centred first), so several show as several.
        int alreadyOpen = _ringing.Count;
        if (alreadyOpen > 0)
        {
            window.Opened += (_, _) =>
            {
                int offset = (int)Math.Round(StackOffset * alreadyOpen * window.DesktopScaling);
                window.Position = new PixelPoint(window.Position.X + offset, window.Position.Y + offset);
            };
        }

        _ringing[id] = ringing;
        // Closed some other way (Alt+F4): counts as Dismiss.
        window.Closed += (_, _) =>
        {
            if (_ringing.TryGetValue(id, out Ringing? current) && current == ringing)
            {
                _ringing.Remove(id);
                onClosedByHand();
                StopSoundIfQuiet();
            }
        };
        window.Show();
        _sound.Start();
    }

    private void RepeatChime()
    {
        DateTime now = DateTime.UtcNow;
        bool anyStillRinging = _ringing.Values.Any(ringing => now - ringing.StartedAt <= ringing.MaxRing);
        if (anyStillRinging)
        {
            _playChime();
        }
        else
        {
            // The windows stay until answered; only the sound stops.
            _sound.Stop();
        }
    }

    private void StopSettled()
    {
        foreach ((Guid id, Ringing ringing) in _ringing.ToList())
        {
            bool settled = ringing.IsAlarm
                ? _timers.FindAlarm(id) is null
                : _timers.FindTimer(id) is not { State: TimerState.Done, Dismissed: false };
            if (settled)
            {
                Stop(id);
            }
        }
    }

    private void Stop(Guid id)
    {
        if (!_ringing.Remove(id, out Ringing? ringing))
        {
            return;
        }

        ringing.Window.Close();
        StopSoundIfQuiet();
    }

    private void StopSoundIfQuiet()
    {
        if (_ringing.Count == 0)
        {
            _sound.Stop();
        }
    }

    private static string DescribeLength(TimeSpan length)
    {
        if (length.TotalHours >= 1)
        {
            return length.Minutes == 0 ? $"{(int)length.TotalHours} h" : $"{(int)length.TotalHours} h {length.Minutes} min";
        }

        return length.Seconds == 0 ? $"{(int)length.TotalMinutes} min" : $"{(int)length.TotalMinutes} min {length.Seconds} s";
    }

    private sealed record Ringing(Window Window, DateTime StartedAt, TimeSpan MaxRing, bool IsAlarm);
}
