using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Storage;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// With sleep mode on, raises <see cref="ReminderDue"/> once per night when bedtime is two hours away (or as soon as
/// the app runs within those two hours). Which night was last reminded is kept (document "sleep.lastReminder") so a
/// restart doesn't remind again. Silent: what to show is up to the listener. Use from the UI thread only.
/// </summary>
public sealed class BedtimeReminderService : IDisposable
{
    public const string DocumentKey = "sleep.lastReminder";

    /// <summary>How long before bedtime the reminder comes.</summary>
    public static readonly TimeSpan Lead = TimeSpan.FromHours(2);

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    private readonly Func<DayPlan> _plan;
    private readonly SleepSettingsService _sleep;
    private readonly SqliteJsonDocumentStore _store;
    private readonly TimeProvider _time;
    private readonly DispatcherTimer _timer;

    // The bedtime of the last night reminded about.
    private DateTime? _lastReminded;

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
    }

    /// <summary>Raised with a title and a message when tonight's reminder is due.</summary>
    public event EventHandler<(string Title, string Message)>? ReminderDue;

    public void LoadBlocking()
    {
        try
        {
            _lastReminded = Task.Run(() => _store.LoadAsync<DateTime?>(DocumentKey)).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            _lastReminded = null;
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

    private void Check()
    {
        SleepSettings settings = _sleep.Current;
        if (!settings.Enabled)
        {
            return;
        }

        DateTime now = TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _time.LocalTimeZone).DateTime;
        LocalWindow night = settings.ToSchedule(_plan()).NightAround(now);
        TimeSpan untilBed = night.Start - now;
        if (night.End <= night.Start || untilBed <= TimeSpan.Zero || untilBed > Lead || _lastReminded == night.Start)
        {
            return;
        }

        _lastReminded = night.Start;
        _ = _store.SaveAsync(DocumentKey, night.Start);
        TimeSpan sleep = night.End - night.Start;
        ReminderDue?.Invoke(this, (
            $"Bedtime in {Duration(untilBed)}",
            $"Bed at {night.Start:t} gives you {Duration(sleep)} of sleep (up at {night.End:t}). Time to start winding down."));
    }

    private static string Duration(TimeSpan span)
    {
        int minutes = (int)Math.Ceiling(span.TotalMinutes);
        int hours = minutes / 60;
        int rest = minutes % 60;
        return hours == 0 ? $"{rest} min" : rest == 0 ? $"{hours} h" : $"{hours} h {rest} min";
    }
}
