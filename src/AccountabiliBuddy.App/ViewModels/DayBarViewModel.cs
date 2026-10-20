using System.Globalization;
using AccountabiliBuddy.App.Controls;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The day bar at the top of the main window: today's planned focus (schedule minus time off), the sessions that
/// actually ran (from the history, plus the running one up to now), calendar events, and a live "now" line.
/// The on-screen timeline uses its own copy, which draws even when the day bar is hidden. Omitted sessions (e.g.
/// tests) are left out.
/// With sleep mode on (and "fit to my day"), the day runs from wake time to bedtime and grows into the night
/// while you're still up; the night is shaded, sleep lost by staying up is shaded red, and from two hours before
/// bedtime <see cref="SleepText"/> says how much sleep is left if you go to bed now.
/// </summary>
public sealed partial class DayBarViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan RedrawInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HistoryReloadInterval = TimeSpan.FromMinutes(5);

    // Session events are written in the background; reading right after a start or end could miss them.
    private static readonly TimeSpan RecorderDelay = TimeSpan.FromSeconds(2);
    private static readonly int[] TickSteps = { 1, 2, 3, 4, 6 };

    // How much of the night the bar shows past bedtime, and past now when still up later.
    private static readonly TimeSpan NightShown = TimeSpan.FromHours(1);
    private static readonly TimeSpan NightAfterNow = TimeSpan.FromMinutes(30);

    // The sleep line shows from this long before bedtime until wake time.
    private static readonly TimeSpan SleepLineLead = TimeSpan.FromHours(2);

    // Soft green, yellow and red: readable on the dark on-screen window, and as a dot in the main window.
    private static readonly IBrush GoodSleepBrush = new SolidColorBrush(Color.FromRgb(134, 239, 172));
    private static readonly IBrush OkaySleepBrush = new SolidColorBrush(Color.FromRgb(253, 230, 138));
    private static readonly IBrush ShortSleepBrush = new SolidColorBrush(Color.FromRgb(252, 165, 165));
    private const int MaxTicks = 8;

    private readonly ConfigurationService _configuration;
    private readonly SessionController _sessions;
    private readonly CalendarService _calendars;
    private readonly IActivityLog _log;
    private readonly DayBarSettingsService _settings;
    private readonly SleepSettingsService _sleep;
    private readonly OmittedSessionsService _omitted;
    private readonly TimeProvider _time;
    private readonly DispatcherTimer _redrawTimer;
    private readonly DispatcherTimer _reloadSoonTimer;
    private readonly bool _ignoresShowSetting;

    private IReadOnlyList<SessionRecord> _records = Array.Empty<SessionRecord>();
    private DateTimeOffset _historyLoadedAt = DateTimeOffset.MinValue;
    private LocalWindow _historySpan;

    public DayBarViewModel(
        ConfigurationService configuration,
        SessionController sessions,
        CalendarService calendars,
        IActivityLog log,
        DayBarSettingsService settings,
        SleepSettingsService sleep,
        OmittedSessionsService omitted,
        TimeProvider time,
        bool ignoresShowSetting = false)
    {
        _omitted = omitted;
        _sleep = sleep;
        _ignoresShowSetting = ignoresShowSetting;
        _configuration = configuration;
        _sessions = sessions;
        _calendars = calendars;
        _log = log;
        _settings = settings;
        _time = time;

        Layout = DayBarLayout.Empty;
        DateText = StatusText = SummaryText = SleepText = string.Empty;
        SleepBrush = GoodSleepBrush;

        _redrawTimer = new DispatcherTimer { Interval = RedrawInterval };
        _redrawTimer.Tick += (_, _) => OnRedrawTick();
        _reloadSoonTimer = new DispatcherTimer { Interval = RecorderDelay };
        _reloadSoonTimer.Tick += (_, _) =>
        {
            _reloadSoonTimer.Stop();
            _ = ReloadHistoryAsync();
        };

        _configuration.Changed += (_, _) => Redraw();
        _calendars.Changed += (_, _) => Redraw();
        _settings.Changed += (_, _) => _ = ReloadHistoryAsync();
        _sleep.Changed += (_, _) => _ = ReloadHistoryAsync();
        _omitted.Changed += (_, _) => Redraw();
        _sessions.StateChanged += (_, _) =>
        {
            Redraw();
            _reloadSoonTimer.Stop();
            _reloadSoonTimer.Start();
        };

        Redraw();
    }

    [ObservableProperty]
    public partial DayBarLayout Layout { get; set; }

    /// <summary>E.g. "Mon (10/19)".</summary>
    [ObservableProperty]
    public partial string DateText { get; set; }

    /// <summary>E.g. "In focus · Coding until 12:00 PM" or "Next: Coding at 1:30 PM".</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; }

    /// <summary>E.g. "3h 10m focused of 4h 30m planned".</summary>
    [ObservableProperty]
    public partial string SummaryText { get; set; }

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    /// <summary>Raised with a profile's id when its block on the bar is clicked.</summary>
    public event EventHandler<Guid>? ProfileRequested;

    [RelayCommand]
    private void OpenProfile(Guid profileId)
    {
        ProfileRequested?.Invoke(this, profileId);
    }

    /// <summary>True from two hours before bedtime until wake time, with sleep mode on.</summary>
    [ObservableProperty]
    public partial bool ShowsSleep { get; set; }

    /// <summary>E.g. "Bedtime 10:00 PM · 8h of sleep" or "Sleep if you go now: 6h 40m".</summary>
    [ObservableProperty]
    public partial string SleepText { get; set; }

    /// <summary>How the sleep in <see cref="SleepText"/> rates.</summary>
    [ObservableProperty]
    public partial SleepAmount SleepAmount { get; set; }

    /// <summary>Green from 7 hours of sleep, yellow from 5, red below.</summary>
    [ObservableProperty]
    public partial IBrush SleepBrush { get; set; }

    /// <summary>True when some focus is planned in the day shown (schedule or one-off blocks, minus time off).</summary>
    [ObservableProperty]
    public partial bool HasPlannedFocus { get; set; }

    public void Start()
    {
        _redrawTimer.Start();
        _ = ReloadHistoryAsync();
    }

    public void Dispose()
    {
        _redrawTimer.Stop();
        _reloadSoonTimer.Stop();
    }

    private void OnRedrawTick()
    {
        bool stale = _time.GetUtcNow() - _historyLoadedAt >= HistoryReloadInterval;
        bool newDay = CurrentDay().Span != _historySpan;
        if (stale || newDay)
        {
            _ = ReloadHistoryAsync();
            return;
        }

        Redraw();
    }

    private async Task ReloadHistoryAsync()
    {
        LocalWindow span = CurrentDay().Span;
        try
        {
            // A day earlier, so a session that started before the bar and ran into it is included.
            IReadOnlyList<SessionEvent> events = await _log.GetSessionEventsAsync(ToOffset(span.Start.AddDays(-1)), ToOffset(span.End));
            _records = SessionHistoryBuilder.Build(events);
            _historySpan = span;
            _historyLoadedAt = _time.GetUtcNow();
        }
        catch (SqliteException)
        {
            // Keep what was shown; the next reload tries again.
        }

        Redraw();
    }

    private void Redraw()
    {
        DayBarSettings settings = _settings.Current;
        IsVisible = settings.Show;
        if (!settings.Show && !_ignoresShowSetting)
        {
            return;
        }

        DateTime now = LocalNow();
        (LocalWindow day, LocalWindow span) = CurrentDay();
        IReadOnlyList<PlannedFocus> planned = DayTimeline.Planned(_configuration.Current.Schedule, span);
        List<(LocalWindow Window, string Tip, Guid? ProfileId)> actual = ActualSessions(now, span);

        IEnumerable<LocalWindow> content = planned.Select(focus => focus.Window).Concat(actual.Select(item => item.Window));
        if (_sleep.Current.Enabled)
        {
            // The calendar dates also hold tomorrow's early blocks; only blocks touching this waking day stretch it.
            content = content.Where(window => DayTimeline.Clip(window, day) is not null);
        }

        LocalWindow range = settings.Range == DayBarRange.FullDay ? day : DayTimeline.FitTo(day, content, span);

        var plannedSpans = new List<DayBarSpan>();
        foreach (PlannedFocus focus in planned)
        {
            if (DayTimeline.Clip(focus.Window, range) is LocalWindow window)
            {
                plannedSpans.Add(ToSpan(window, range, $"{ProfileName(focus.ProfileId)} · planned {Times(window)} · click to open", focus.ProfileId));
            }
        }

        var actualSpans = new List<DayBarSpan>();
        foreach ((LocalWindow window, string tip, Guid? profileId) in actual)
        {
            if (DayTimeline.Clip(window, range) is LocalWindow clipped)
            {
                actualSpans.Add(ToSpan(clipped, range, tip, profileId));
            }
        }

        (List<DayBarSpan> night, List<DayBarSpan> missed) = SleepSpans(now, range);
        Layout = new DayBarLayout(plannedSpans, actualSpans, EventSpans(range), Ticks(range), Fraction(now, range), night, missed);
        UpdateSleepLine(now);
        HasPlannedFocus = plannedSpans.Count > 0;

        DateTime date = day.Start.Date;
        DateText = $"{date.ToString("ddd", CultureInfo.CurrentCulture)} ({date:M/d})";
        StatusText = Status(now, range, planned);
        SummaryText = Summary(now, range, planned, actual);
    }

    /// <summary>The day the bar shows, and the whole calendar dates it touches.</summary>
    private (LocalWindow Day, LocalWindow Span) CurrentDay()
    {
        DayBarSettings settings = _settings.Current;
        DateTime now = LocalNow();
        LocalWindow day = settings.Range == DayBarRange.FullDay ? DayTimeline.FullDay(now)
            : _sleep.Current.Enabled ? WakingDay(now)
            : DayTimeline.DayContaining(now, settings.DayStart, settings.DayEnd);
        return (day, DayTimeline.CalendarSpan(day));
    }

    /// <summary>Wake time to bedtime, plus a little of the night (more while still up, until wake time).</summary>
    private LocalWindow WakingDay(DateTime now)
    {
        SleepSchedule schedule = _sleep.Current.ToSchedule();
        LocalWindow waking = schedule.DayAround(now);
        LocalWindow night = schedule.NightAround(now);
        DateTime shownUntil = night.Start + NightShown > now + NightAfterNow ? night.Start + NightShown : now + NightAfterNow;
        return new LocalWindow(waking.Start, shownUntil < night.End ? shownUntil : night.End);
    }

    /// <summary>The nights inside the bar, and the part of the current night already lost to staying up.</summary>
    private (List<DayBarSpan> Night, List<DayBarSpan> Missed) SleepSpans(DateTime now, LocalWindow range)
    {
        var nights = new List<DayBarSpan>();
        var missed = new List<DayBarSpan>();
        if (!_sleep.Current.Enabled)
        {
            return (nights, missed);
        }

        SleepSchedule schedule = _sleep.Current.ToSchedule();
        for (DateTime evening = range.Start.Date.AddDays(-1); evening <= range.End.Date; evening = evening.AddDays(1))
        {
            LocalWindow night = schedule.Night(DateOnly.FromDateTime(evening));
            if (DayTimeline.Clip(night, range) is LocalWindow shown)
            {
                nights.Add(ToSpan(shown, range, $"Sleep {Times(night)} ({Duration(night.End - night.Start)})"));
            }
        }

        LocalWindow current = schedule.NightAround(now);
        if (now > current.Start)
        {
            var lost = new LocalWindow(current.Start, now < current.End ? now : current.End);
            if (DayTimeline.Clip(lost, range) is LocalWindow shownLost)
            {
                missed.Add(ToSpan(shownLost, range, $"Up past bedtime: {Duration(lost.End - lost.Start)} less sleep"));
            }
        }

        return (nights, missed);
    }

    private void UpdateSleepLine(DateTime now)
    {
        SleepSchedule schedule = _sleep.Current.ToSchedule();
        LocalWindow night = schedule.NightAround(now);
        ShowsSleep = _sleep.Current.Enabled && now >= night.Start - SleepLineLead && now < night.End;
        if (!ShowsSleep)
        {
            return;
        }

        TimeSpan sleep = SleepSchedule.SleepIfBedAt(now, night);
        SleepAmount = SleepSchedule.Rate(sleep);
        SleepBrush = SleepAmount switch
        {
            SleepAmount.Good => GoodSleepBrush,
            SleepAmount.Okay => OkaySleepBrush,
            _ => ShortSleepBrush,
        };
        SleepText = now < night.Start
            ? $"Bedtime {night.Start:t} · {Duration(sleep)} of sleep"
            : $"Sleep if you go now: {Duration(sleep)}";
    }

    private List<(LocalWindow Window, string Tip, Guid? ProfileId)> ActualSessions(DateTime now, LocalWindow span)
    {
        var sessions = new List<(LocalWindow Window, string Tip, Guid? ProfileId)>();
        SessionState state = _sessions.State;
        bool runningShown = false;

        for (int i = 0; i < _records.Count; i++)
        {
            SessionRecord record = _records[i];
            if (_omitted.IsOmitted(record.StartedAt))
            {
                // Omitted (e.g. a test): it doesn't count (the running one is also left out).
                runningShown |= i == 0 && state.IsActive && record.EndedAt is null;
                continue;
            }

            DateTime start = ToLocal(record.StartedAt);
            string profile = ProfileName(record.ProfileId);

            if (record.EndedAt is DateTimeOffset ended)
            {
                var window = new LocalWindow(start, ToLocal(ended));
                sessions.Add((window, $"{profile} · ran {Times(window)}", record.ProfileId));
            }
            else if (i == 0 && state.IsActive)
            {
                // The newest session without an end is the one running now.
                sessions.Add((new LocalWindow(start, now), $"{profile} · running since {start:t}", record.ProfileId));
                runningShown = true;
            }

            // Older sessions without an end stopped when the app did; when isn't known, so they're left out.
        }

        if (state.IsActive && !runningShown)
        {
            // Not in the history (history off, or not written yet): show the current phase.
            DateTime start = state.PhaseStartedAt is DateTimeOffset phaseStart ? ToLocal(phaseStart) : now;
            sessions.Add((new LocalWindow(start, now), $"{ProfileName(state.ProfileId)} · running since {start:t}", state.ProfileId));
        }

        return sessions.Where(item => DayTimeline.Clip(item.Window, span) is not null).ToList();
    }

    private List<DayBarSpan> EventSpans(LocalWindow range)
    {
        var spans = new List<DayBarSpan>();
        var seen = new HashSet<(string, string)>();
        for (DateTime date = range.Start.Date; date < range.End; date = date.AddDays(1))
        {
            foreach (CalendarEntry entry in _calendars.EventsOn(DateOnly.FromDateTime(date)))
            {
                CalendarEvent calendarEvent = entry.Event;
                if (!seen.Add((calendarEvent.CalendarId, calendarEvent.Id)))
                {
                    continue;
                }

                var window = new LocalWindow(calendarEvent.LocalStart, calendarEvent.LocalEnd > calendarEvent.LocalStart
                    ? calendarEvent.LocalEnd
                    : calendarEvent.LocalStart.AddMinutes(1));
                if (DayTimeline.Clip(window, range) is LocalWindow clipped)
                {
                    string when = calendarEvent.IsAllDay ? "all day" : Times(window);
                    spans.Add(ToSpan(clipped, range, $"{calendarEvent.Title} · {when}"));
                }
            }
        }

        return spans;
    }

    private string Status(DateTime now, LocalWindow range, IReadOnlyList<PlannedFocus> planned)
    {
        SessionState state = _sessions.State;
        if (state.IsActive)
        {
            string profile = ProfileName(state.ProfileId);
            string? until = state.PhaseEndsAt is DateTimeOffset end ? $"{ToLocal(end):t}" : null;
            return state.IsEnforcing
                ? until is null ? $"In focus · {profile}, no set end" : $"In focus · {profile} until {until}"
                : until is null ? "On a break" : $"On a break until {until}";
        }

        PlannedFocus? current = planned.FirstOrDefault(focus => focus.Window.Contains(now));
        if (current is not null)
        {
            return $"{ProfileName(current.ProfileId)} planned until {current.Window.End:t} · not running";
        }

        PlannedFocus? next = planned.FirstOrDefault(focus => focus.Window.Start > now && focus.Window.Start < range.End);
        if (next is not null)
        {
            return $"Next: {ProfileName(next.ProfileId)} at {next.Window.Start:t}";
        }

        return planned.Any(focus => DayTimeline.Clip(focus.Window, range) is not null)
            ? "No more focus planned today"
            : "No focus blocks today";
    }

    private static string Summary(
        DateTime now, LocalWindow range, IReadOnlyList<PlannedFocus> planned, List<(LocalWindow Window, string Tip, Guid? ProfileId)> actual)
    {
        TimeSpan plannedTotal = Total(planned.Select(focus => focus.Window), range);
        TimeSpan focusedTotal = Total(actual.Select(item => item.Window), range with { End = now < range.End ? now : range.End });
        return plannedTotal > TimeSpan.Zero
            ? $"{Duration(focusedTotal)} focused of {Duration(plannedTotal)} planned"
            : $"{Duration(focusedTotal)} focused";
    }

    private static TimeSpan Total(IEnumerable<LocalWindow> windows, LocalWindow range)
    {
        IEnumerable<LocalWindow> clipped = windows
            .Select(window => DayTimeline.Clip(window, range))
            .OfType<LocalWindow>();
        return DayTimeline.Union(clipped).Aggregate(TimeSpan.Zero, (sum, window) => sum + (window.End - window.Start));
    }

    private static List<DayBarTick> Ticks(LocalWindow range)
    {
        double hours = (range.End - range.Start).TotalHours;
        int step = TickSteps.FirstOrDefault(candidate => hours / candidate <= MaxTicks, TickSteps[^1]);
        bool twelveHour = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('t');

        var ticks = new List<DayBarTick>();
        DateTime first = range.Start.Date.AddHours(range.Start.Hour);
        if (first < range.Start)
        {
            first = first.AddHours(1);
        }

        for (DateTime tick = first; tick <= range.End; tick = tick.AddHours(1))
        {
            if (tick.Hour % step == 0)
            {
                ticks.Add(new DayBarTick(Fraction(tick, range), HourLabel(tick.Hour, twelveHour)));
            }
        }

        return ticks;
    }

    private static string HourLabel(int hour, bool twelveHour)
    {
        if (!twelveHour)
        {
            return $"{hour:00}:00";
        }

        int shown = hour % 12 == 0 ? 12 : hour % 12;
        return $"{shown}{(hour < 12 ? "a" : "p")}";
    }

    private static DayBarSpan ToSpan(LocalWindow window, LocalWindow range, string tip, Guid? profileId = null)
    {
        return new DayBarSpan(Fraction(window.Start, range), Fraction(window.End, range), tip, profileId);
    }

    private static double Fraction(DateTime time, LocalWindow range)
    {
        double length = (range.End - range.Start).TotalMinutes;
        return length <= 0 ? 0 : (time - range.Start).TotalMinutes / length;
    }

    private static string Times(LocalWindow window)
    {
        return $"{window.Start:t} – {window.End:t}";
    }

    private static string Duration(TimeSpan duration)
    {
        int minutes = (int)Math.Round(duration.TotalMinutes);
        return minutes >= 60 ? $"{minutes / 60}h {minutes % 60:00}m" : $"{minutes}m";
    }

    private string ProfileName(Guid? profileId)
    {
        return ProfileOptions.NameOf(_configuration.Current.Profiles, profileId) ?? "(deleted profile)";
    }

    private DateTime LocalNow()
    {
        return _time.GetLocalNow().DateTime;
    }

    private DateTime ToLocal(DateTimeOffset time)
    {
        return TimeZoneInfo.ConvertTime(time, _time.LocalTimeZone).DateTime;
    }

    private DateTimeOffset ToOffset(DateTime local)
    {
        return new DateTimeOffset(local, _time.LocalTimeZone.GetUtcOffset(local));
    }
}
