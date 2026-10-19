using System.Globalization;
using AccountabiliBuddy.App.Controls;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The day bar at the top of the main window: today's planned focus (schedule minus time off), the sessions that
/// actually ran (from the history, plus the running one up to now), calendar events, and a live "now" line.
/// The on-screen timeline uses its own copy, which draws even when the day bar is hidden.
/// </summary>
public sealed partial class DayBarViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan RedrawInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HistoryReloadInterval = TimeSpan.FromMinutes(5);

    // Session events are written in the background; reading right after a start or end could miss them.
    private static readonly TimeSpan RecorderDelay = TimeSpan.FromSeconds(2);
    private static readonly int[] TickSteps = { 1, 2, 3, 4, 6 };
    private const int MaxTicks = 8;

    private readonly ConfigurationService _configuration;
    private readonly SessionController _sessions;
    private readonly CalendarService _calendars;
    private readonly IActivityLog _log;
    private readonly DayBarSettingsService _settings;
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
        TimeProvider time,
        bool ignoresShowSetting = false)
    {
        _ignoresShowSetting = ignoresShowSetting;
        _configuration = configuration;
        _sessions = sessions;
        _calendars = calendars;
        _log = log;
        _settings = settings;
        _time = time;

        Layout = DayBarLayout.Empty;
        DateText = StatusText = SummaryText = string.Empty;

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
        List<(LocalWindow Window, string Tip)> actual = ActualSessions(now, span);

        LocalWindow range = settings.Range == DayBarRange.FullDay
            ? day
            : DayTimeline.FitTo(day, planned.Select(focus => focus.Window).Concat(actual.Select(item => item.Window)), span);

        var plannedSpans = new List<DayBarSpan>();
        foreach (PlannedFocus focus in planned)
        {
            if (DayTimeline.Clip(focus.Window, range) is LocalWindow window)
            {
                plannedSpans.Add(ToSpan(window, range, $"{ProfileName(focus.ProfileId)} · planned {Times(window)}"));
            }
        }

        var actualSpans = new List<DayBarSpan>();
        foreach ((LocalWindow window, string tip) in actual)
        {
            if (DayTimeline.Clip(window, range) is LocalWindow clipped)
            {
                actualSpans.Add(ToSpan(clipped, range, tip));
            }
        }

        Layout = new DayBarLayout(plannedSpans, actualSpans, EventSpans(range), Ticks(range), Fraction(now, range));
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
        LocalWindow day = settings.Range == DayBarRange.FullDay
            ? DayTimeline.FullDay(now)
            : DayTimeline.DayContaining(now, settings.DayStart, settings.DayEnd);
        return (day, DayTimeline.CalendarSpan(day));
    }

    private List<(LocalWindow Window, string Tip)> ActualSessions(DateTime now, LocalWindow span)
    {
        var sessions = new List<(LocalWindow Window, string Tip)>();
        SessionState state = _sessions.State;
        bool runningShown = false;

        for (int i = 0; i < _records.Count; i++)
        {
            SessionRecord record = _records[i];
            DateTime start = ToLocal(record.StartedAt);
            string profile = ProfileName(record.ProfileId);

            if (record.EndedAt is DateTimeOffset ended)
            {
                var window = new LocalWindow(start, ToLocal(ended));
                sessions.Add((window, $"{profile} · ran {Times(window)}"));
            }
            else if (i == 0 && state.IsActive)
            {
                // The newest session without an end is the one running now.
                sessions.Add((new LocalWindow(start, now), $"{profile} · running since {start:t}"));
                runningShown = true;
            }

            // Older sessions without an end stopped when the app did; when isn't known, so they're left out.
        }

        if (state.IsActive && !runningShown)
        {
            // Not in the history (history off, or not written yet): show the current phase.
            DateTime start = state.PhaseStartedAt is DateTimeOffset phaseStart ? ToLocal(phaseStart) : now;
            sessions.Add((new LocalWindow(start, now), $"{ProfileName(state.ProfileId)} · running since {start:t}"));
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
        DateTime now, LocalWindow range, IReadOnlyList<PlannedFocus> planned, List<(LocalWindow Window, string Tip)> actual)
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

    private static DayBarSpan ToSpan(LocalWindow window, LocalWindow range, string tip)
    {
        return new DayBarSpan(Fraction(window.Start, range), Fraction(window.End, range), tip);
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
