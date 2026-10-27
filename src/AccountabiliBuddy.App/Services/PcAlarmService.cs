using System.Globalization;
using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.App.Views;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// This PC's alarm (ADR 35), when Settings → Sleep takes the wake time from it: rings at the night's wake time as the
/// plan works it out (the time-up sound every 10 seconds, the on-screen window's glow and a window with Stop and Snooze
/// 9 min), and sets a Windows wake timer for it so a sleeping PC wakes to ring. A wake time missed by more than an hour
/// (the PC was off) isn't rung. Use from the UI thread only.
/// </summary>
public sealed class PcAlarmService
{
    private static readonly TimeSpan TooLate = TimeSpan.FromHours(1);
    private static readonly TimeSpan RepeatSound = TimeSpan.FromSeconds(10);

    private readonly SleepSettingsService _sleep;
    private readonly ConfigurationService _configuration;
    private readonly SessionCueService _cues;
    private readonly IWakeTimer _wakeTimer;
    private readonly TimeProvider _time;
    private readonly Action _glow;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly DispatcherTimer _sound;

    // The next time to ring (a wake time or the end of a snooze), and the last one rung.
    private DateTime? _next;
    private DateTime? _rung;
    private DateTime? _snoozeUntil;
    private DateTime? _timerSetFor;
    private Window? _window;

    public PcAlarmService(
        SleepSettingsService sleep, ConfigurationService configuration, SessionCueService cues, IWakeTimer wakeTimer, TimeProvider time, Action glow)
    {
        _sleep = sleep;
        _configuration = configuration;
        _cues = cues;
        _wakeTimer = wakeTimer;
        _time = time;
        _glow = glow;
        _sound = new DispatcherTimer { Interval = RepeatSound };
        _sound.Tick += (_, _) => PlaySound();
    }

    private bool IsOn => _sleep.Current.Enabled && _sleep.Current.UsePcAlarm;

    private DateTime Now => _time.GetLocalNow().DateTime;

    public void Start()
    {
        _tick.Tick += (_, _) => Check();
        _sleep.Changed += (_, _) => Check();
        _configuration.Changed += (_, _) => Check();
        // Just launched or woken: a wake time a few minutes ago still rings.
        if (IsOn)
        {
            DateTime lastWake = _sleep.Schedule(_configuration.Current.DayPlan).Night(DateOnly.FromDateTime(Now).AddDays(-1)).End;
            if (Now >= lastWake && Now - lastWake < TimeSpan.FromMinutes(10))
            {
                _next = lastWake;
            }
        }

        Check();
        _tick.Start();
    }

    /// <summary>Why the PC may not wake to ring; null when it should (for Settings).</summary>
    public string? WakeProblem() => _wakeTimer.Problem();

    private void Check()
    {
        if (!IsOn)
        {
            _next = null;
            _snoozeUntil = null;
            CancelTimer();
            return;
        }

        DateTime now = Now;
        if (_next is DateTime due && now >= due && _rung != due)
        {
            _rung = due;
            _snoozeUntil = null;
            if (now - due < TooLate)
            {
                Ring(due);
            }
        }

        // The night's wake time ahead (or the snooze), worked out again so plan and sleep changes count.
        DateTime wake = _sleep.Schedule(_configuration.Current.DayPlan).NightAround(now).End;
        _next = _snoozeUntil is DateTime snooze && snooze > now ? snooze : wake > now ? wake : _next;
        if (_next is DateTime upcoming && upcoming > now && upcoming != _timerSetFor)
        {
            _timerSetFor = _wakeTimer.WakeAt(new DateTimeOffset(upcoming)) ? upcoming : null;
        }
    }

    private void Ring(DateTime due)
    {
        _glow();
        PlaySound();
        _sound.Start();
        if (_window is not null)
        {
            _window.Activate();
            return;
        }

        var viewModel = new AlarmRingViewModel(
            $"Good morning · {due.ToString("h:mm tt", CultureInfo.InvariantCulture)}",
            $"Your wake time. Snooze rings again in {AlarmRingViewModel.DefaultSnoozeMinutes} minutes, or pick another length.",
            "Stop", () => Now, onStop: Stop, onSnooze: SnoozeNow);
        _window = new AlarmRingWindow { DataContext = viewModel };
        _window.Closed += (_, _) =>
        {
            viewModel.Dispose();
            _window = null;
            _sound.Stop();
        };
        _window.Show();
        _window.Activate();
    }

    private void PlaySound()
    {
        _cues.Play(SoundChoice.BellId, 100);
    }

    private void Stop()
    {
        _sound.Stop();
        _window?.Close();
    }

    private void SnoozeNow(int minutes)
    {
        _snoozeUntil = Now.AddMinutes(minutes);
        _next = _snoozeUntil;
        Stop();
        Check();
    }

    private void CancelTimer()
    {
        if (_timerSetFor is not null)
        {
            _wakeTimer.Cancel();
            _timerSetFor = null;
        }
    }
}
