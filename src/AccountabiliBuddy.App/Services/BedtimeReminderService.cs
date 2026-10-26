using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Storage;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Tonight's bedtime reminders on this PC (ADR 38): the ones addressed to it ("All PCs" or this PC) fire at bedtime
/// minus their minutes, worked out again on every check so they follow bedtime when it moves (an alarm, the smart
/// bedtime, a one-night change). A notification raises <see cref="ReminderDue"/>; an alarm raises
/// <see cref="AlarmDue"/>. What fired, and snoozes, are kept for the night (document "sleep.reminders") so a restart
/// doesn't fire again. Use from the UI thread only.
/// </summary>
public sealed class BedtimeReminderService : IDisposable
{
    public const string DocumentKey = "sleep.reminders";

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan Snooze = TimeSpan.FromMinutes(5);

    private readonly Func<DayPlan> _plan;
    private readonly SleepSettingsService _sleep;
    private readonly SqliteJsonDocumentStore _store;
    private readonly TimeProvider _time;
    private readonly DispatcherTimer _timer;
    private NightState _state = new(string.Empty, new List<int>(), new Dictionary<int, DateTime>());

    /// <param name="plan">The current day plan, which sets each night's bedtime.</param>
    public BedtimeReminderService(SleepSettingsService sleep, SqliteJsonDocumentStore store, TimeProvider time, Func<DayPlan> plan)
    {
        _plan = plan;
        _sleep = sleep;
        _store = store;
        _time = time;
        _timer = new DispatcherTimer { Interval = CheckInterval };
        _timer.Tick += (_, _) => Check();
        _sleep.Changed += (_, _) => Check();
        _sleep.PhoneAlarmChanged += (_, _) => Check();
    }

    /// <summary>This PC's sync device id (for reminders addressed to it); set by the app.</summary>
    public Func<Guid?> DeviceId { get; set; } = () => null;

    /// <summary>A notification reminder: its title and message.</summary>
    public event EventHandler<(string Title, string Message)>? ReminderDue;

    /// <summary>An alarm reminder: ring it (the ringer calls <see cref="Dismiss"/> or <see cref="SnoozeIt"/>).</summary>
    public event EventHandler<BedtimeAlarm>? AlarmDue;

    /// <summary>A reminder was dismissed or snoozed here (for the other devices, ADR 38).</summary>
    public event EventHandler<(string Night, int Number, string Action, DateTimeOffset? Until)>? ActedHere;

    /// <summary>A reminder dismissed or snoozed on another device: stop it ringing here.</summary>
    public event EventHandler<int>? StopRinging;

    public void LoadBlocking()
    {
        try
        {
            _state = Task.Run(() => _store.LoadAsync<NightState>(DocumentKey)).GetAwaiter().GetResult() ?? _state;
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            // Starts the night over.
        }
    }

    public void Start()
    {
        _timer.Start();
        Check();
    }

    public void Dispose()
    {
        _timer.Stop();
    }

    /// <summary>Stops reminder <paramref name="number"/> tonight; <paramref name="here"/> = done on this PC (tell the others).</summary>
    public void Dismiss(int number, bool here = true)
    {
        _state.Snoozes.Remove(number);
        Save();
        if (here)
        {
            ActedHere?.Invoke(this, (_state.Night, number, "dismiss", null));
        }
        else
        {
            StopRinging?.Invoke(this, number);
        }
    }

    /// <summary>Rings reminder <paramref name="number"/> again at <paramref name="until"/> (5 minutes from now when null).</summary>
    public void SnoozeIt(int number, bool here = true, DateTimeOffset? until = null)
    {
        DateTimeOffset at = until ?? _time.GetUtcNow() + Snooze;
        _state.Snoozes[number] = TimeZoneInfo.ConvertTime(at, _time.LocalTimeZone).DateTime;
        Save();
        if (here)
        {
            ActedHere?.Invoke(this, (_state.Night, number, "snooze", at));
        }
        else
        {
            StopRinging?.Invoke(this, number);
        }
    }

    /// <summary>The night id reminders are counted for now ("2026-10-25").</summary>
    public string Night => _state.Night;

    private void Check()
    {
        SleepSettings settings = _sleep.Current;
        Guid? device = DeviceId();
        if (!settings.Enabled || device is not Guid me)
        {
            return;
        }

        DateTime now = TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _time.LocalTimeZone).DateTime;
        LocalWindow night = _sleep.Schedule(_plan()).NightAround(now);
        if (night.End <= night.Start || now >= night.Start)
        {
            FireSnoozes(now, null);
            return;
        }

        string id = BedtimeReminderTimes.NightId(night.Start);
        if (_state.Night != id)
        {
            _state = new NightState(id, new List<int>(), new Dictionary<int, DateTime>());
            Save();
        }

        IReadOnlyList<BedtimeReminderTimes.Due> due = BedtimeReminderTimes.For(night.Start, settings.Reminders);
        foreach (BedtimeReminderTimes.Due reminder in due.Where(d => d.Reminder.ForPc(me)))
        {
            if (_state.Fired.Contains(reminder.Number) || reminder.At > now || now - reminder.At > BedtimeReminderTimes.Grace)
            {
                continue;
            }

            _state.Fired.Add(reminder.Number);
            Save();
            Raise(reminder, night, now);
        }

        FireSnoozes(now, due);
    }

    private void FireSnoozes(DateTime now, IReadOnlyList<BedtimeReminderTimes.Due>? due)
    {
        foreach ((int number, DateTime at) in _state.Snoozes.Where(s => s.Value <= now).ToList())
        {
            _state.Snoozes.Remove(number);
            Save();
            BedtimeReminderTimes.Due? reminder = due?.FirstOrDefault(d => d.Number == number);
            AlarmDue?.Invoke(this, new BedtimeAlarm(number, reminder?.Count ?? number, $"Bedtime · {number}/{reminder?.Count ?? number}", "Snoozed reminder: time for bed."));
        }
    }

    private void Raise(BedtimeReminderTimes.Due reminder, LocalWindow night, DateTime now)
    {
        string label = $"Bedtime · {reminder.Number}/{reminder.Count}";
        TimeSpan left = night.Start - now;
        string text = left <= TimeSpan.FromMinutes(1)
            ? $"It's bedtime ({night.Start:t}). {Duration(night.End - night.Start)} of sleep until {night.End:t}."
            : $"Bedtime in {Duration(left)} ({night.Start:t}). Up at {night.End:t}.";
        if (reminder.Reminder.Alarm)
        {
            AlarmDue?.Invoke(this, new BedtimeAlarm(reminder.Number, reminder.Count, label, text));
        }
        else
        {
            ReminderDue?.Invoke(this, (label, text));
        }
    }

    private async void Save()
    {
        try
        {
            await _store.SaveAsync(DocumentKey, _state);
        }
        catch (SqliteException)
        {
            // Kept in memory; the next change saves again.
        }
    }

    private static string Duration(TimeSpan span)
    {
        int minutes = (int)Math.Ceiling(span.TotalMinutes);
        int hours = minutes / 60;
        int rest = minutes % 60;
        return hours == 0 ? $"{rest} min" : rest == 0 ? $"{hours} h" : $"{hours} h {rest} min";
    }

    /// <summary>Tonight: which reminders fired and which are snoozed until when.</summary>
    private sealed record NightState(string Night, List<int> Fired, Dictionary<int, DateTime> Snoozes);
}

/// <summary>A bedtime reminder that rings: which (n of N), its label and text.</summary>
public sealed record BedtimeAlarm(int Number, int Count, string Label, string Text);
