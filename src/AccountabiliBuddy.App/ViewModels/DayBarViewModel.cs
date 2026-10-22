using System.Globalization;
using AccountabiliBuddy.App.Controls;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.Services.Sync;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Import;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The day bar at the top of the main window, as rows chosen in Settings (<see cref="TimelineRowsBuilder"/>): events
/// from the calendars chosen for it, the day plan's parts, the focus sessions you started (from the history, plus the
/// running one up to now; scheduled blocks block but aren't focus) with drift marks, and Cold Turkey's blocks; all on
/// one hour axis with a live "now" line. The status and the fit follow the plan's Watch parts (planned work).
/// The on-screen timeline uses its own copy, which draws even when the day bar is hidden. Omitted sessions (e.g.
/// tests) are left out, with their drift; so is app or site time deleted from History.
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

    // Coming back to the same site within this long is one visit: one drift mark.
    private static readonly TimeSpan DriftGap = TimeSpan.FromMinutes(2);
    private static readonly int[] TickSteps = { 1, 2, 3, 4, 6 };

    // How much of the night the bar shows past bedtime, and past now when still up later.
    private static readonly TimeSpan NightShown = TimeSpan.FromHours(1);
    private static readonly TimeSpan NightAfterNow = TimeSpan.FromMinutes(30);


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
    private readonly ColdTurkeyService _coldTurkey;
    private ReceivedDevicesService? _received;
    private readonly DeletedUsageService _deleted;
    private readonly TimeProvider _time;
    private readonly DispatcherTimer _redrawTimer;
    private readonly DispatcherTimer _reloadSoonTimer;
    private readonly bool _ignoresShowSetting;

    private IReadOnlyList<SessionRecord> _records = Array.Empty<SessionRecord>();
    private IReadOnlyList<DriftStretch> _drift = Array.Empty<DriftStretch>();
    private SessionTasksService? _tasks;
    private DateTimeOffset _historyLoadedAt = DateTimeOffset.MinValue;
    private LocalWindow _historySpan;

    // Nothing shows this copy right now (the main window is closed to the tray): no redraws, no history reads.
    private bool _paused;

    public DayBarViewModel(
        ConfigurationService configuration,
        SessionController sessions,
        CalendarService calendars,
        IActivityLog log,
        DayBarSettingsService settings,
        SleepSettingsService sleep,
        OmittedSessionsService omitted,
        ColdTurkeyService coldTurkey,
        DeletedUsageService deleted,
        TimeProvider time,
        bool ignoresShowSetting = false)
    {
        _omitted = omitted;
        _coldTurkey = coldTurkey;
        _deleted = deleted;
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
        PlanTotals = Array.Empty<PlanTotal>();
        FocusTotals = Array.Empty<PlanTotal>();
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
        _sleep.PhoneAlarmChanged += (_, _) => _ = ReloadHistoryAsync();
        _omitted.Changed += (_, _) => _ = ReloadHistoryAsync();
        _coldTurkey.Changed += (_, _) => Redraw();
        _deleted.Changed += (_, _) => _ = ReloadHistoryAsync();
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

    /// <summary>E.g. "3h 10m focused · 4h 30m of work planned".</summary>
    [ObservableProperty]
    public partial string SummaryText { get; set; }

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    /// <summary>Raised with a profile's id when one of its sessions on the bar is clicked.</summary>
    public event EventHandler<Guid>? ProfileRequested;

    /// <summary>Raised when a part on the Plan row is clicked.</summary>
    public event EventHandler? DayPlanRequested;

    /// <summary>Raised with the local time clicked on empty Focus-row time: add a past session there (ADR 20).</summary>
    public event EventHandler<DateTime>? AddSessionRequested;

    [RelayCommand]
    private void AddSession(DateTime at)
    {
        AddSessionRequested?.Invoke(this, at);
    }

    /// <summary>Reads the history again soon (e.g. after a session was added by hand).</summary>
    /// <summary>
    /// The other devices' sessions (ADR 22) join the Focus row in their device's shade, and the focused time counts
    /// them where this PC had no session running. Set once, after the sync services are built.
    /// </summary>
    /// <summary>Guided sessions (ADR 24): ticked tasks become white marks on the Focus row.</summary>
    public void UseTasks(SessionTasksService tasks)
    {
        _tasks = tasks;
        tasks.Changed += (_, _) => Dispatcher.UIThread.Post(Redraw);
        Redraw();
    }

    public void UseReceived(ReceivedDevicesService received)
    {
        _received = received;
        received.Changed += (_, _) => Redraw();
        Redraw();
    }

    public void ReloadHistory()
    {
        _ = ReloadHistoryAsync();
    }

    [RelayCommand]
    private void OpenProfile(Guid profileId)
    {
        ProfileRequested?.Invoke(this, profileId);
    }

    [RelayCommand]
    private void OpenDayPlan()
    {
        DayPlanRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>True from when bed now would give the sleep goal until wake time, with sleep mode on.</summary>
    [ObservableProperty]
    public partial bool ShowsSleep { get; set; }

    /// <summary>E.g. "Sleep if you go now: 7h 30m · up 7:00 AM".</summary>
    [ObservableProperty]
    public partial string SleepText { get; set; }

    /// <summary>How the sleep in <see cref="SleepText"/> rates.</summary>
    [ObservableProperty]
    public partial SleepAmount SleepAmount { get; set; }

    /// <summary>Green down to an hour under the sleep goal, yellow down to three hours under, red below.</summary>
    [ObservableProperty]
    public partial IBrush SleepBrush { get; set; }

    /// <summary>True when the day shown has planned work (Watch parts of the day plan).</summary>
    [ObservableProperty]
    public partial bool HasPlannedFocus { get; set; }

    /// <summary>Under the bar: the planned work by kind, e.g. "Work 7h 00m", "Extra Work 2h 00m", in their colours.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<PlanTotal> PlanTotals { get; set; }

    /// <summary>True while the Plan row shows and the day has planned work.</summary>
    [ObservableProperty]
    public partial bool ShowsPlanTotals { get; set; }

    /// <summary>
    /// Under the bar, each in its colour: "3h 10m focused", "Distracted 12m" (time on distracting sites inside this PC's
    /// sessions), "Not in a session 1h 20m" (work time with no session, so far), "4 tasks done".
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<PlanTotal> FocusTotals { get; set; }



    /// <summary>True while the Focus row shows.</summary>
    [ObservableProperty]
    public partial bool ShowsFocusTotal { get; set; }

    public void Start()
    {
        _redrawTimer.Start();
        _ = ReloadHistoryAsync();
    }

    /// <summary>Stops redrawing while nothing shows the bar; <see cref="Resume"/> catches up at once.</summary>
    public void Pause()
    {
        _paused = true;
        _redrawTimer.Stop();
        _reloadSoonTimer.Stop();
    }

    public void Resume()
    {
        if (!_paused)
        {
            return;
        }

        _paused = false;
        Start();
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
        if (_paused)
        {
            return;
        }

        LocalWindow span = CurrentDay().Span;
        try
        {
            // A day either side, so a session that ran into or out of the bar has both its start and end.
            IReadOnlyList<SessionEvent> events = await _log.GetSessionEventsAsync(ToOffset(span.Start.AddDays(-1)), ToOffset(span.End.AddDays(1)));
            _records = SessionHistoryBuilder.Build(events);
            _drift = await _log.GetDriftAsync(ToOffset(span.Start), ToOffset(span.End), LeftOut(), _deleted.Parts);
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
        if (_paused)
        {
            return;
        }

        DayBarSettings settings = _settings.Current;
        IsVisible = settings.Show;
        if (!settings.Show && !_ignoresShowSetting)
        {
            return;
        }

        DateTime now = LocalNow();
        (LocalWindow day, LocalWindow span) = CurrentDay();
        IReadOnlyList<PlannedFocus> planned = DayTimeline.Planned(_configuration.Current.DayPlan, span);
        List<(LocalWindow Window, string Tip, Guid? ProfileId, bool Added)> actual = ActualSessions(now, span);

        IEnumerable<LocalWindow> content = planned.Select(focus => focus.Window).Concat(actual.Select(item => item.Window));
        if (_sleep.Current.Enabled)
        {
            // The calendar dates also hold tomorrow's early parts; only parts touching this waking day stretch it.
            content = content.Where(window => DayTimeline.Clip(window, day) is not null);
        }

        LocalWindow range = settings.Range == DayBarRange.FullDay ? day : DayTimeline.FitTo(day, content, span);

        List<TimelineSession> sessions = actual.Select(item => new TimelineSession(item.Window, item.Tip, item.ProfileId, item.Added)).ToList();
        List<TimelineSession> received = ReceivedOnBar(span);
        // Totals add up across devices; time two devices both had in focus counts once (the union of the windows).
        // Time the user stepped away from this PC's sessions doesn't count.
        TimeSpan focused = Total(DayTimeline.Subtract(
                actual.Select(item => item.Window).Concat(received.Select(session => session.Window)), AwayWindows(now)),
            range with { End = now < range.End ? now : range.End });
        // Drift is this PC's own: it stays inside this PC's sessions.
        IReadOnlyList<TimelineItem> drift = TimelineRowsBuilder.DriftOnBar(DriftItems(), sessions, range);
        // Work time (the plan's Watch parts) so far with no session on any device: its own category on the Focus row.
        var soFar = range with { End = now < range.End ? now : range.End };
        IReadOnlyList<LocalWindow> unfocused = DayTimeline.Subtract(
            planned.Select(focus => DayTimeline.Clip(focus.Window, soFar)).OfType<LocalWindow>(),
            actual.Select(item => item.Window).Concat(received.Select(session => session.Window)));
        TimeSpan distracted = DistractedTime(sessions, soFar);
        IReadOnlyList<TimelineItem> taskMarks = TaskMarks(range);
        var timelineDay = new TimelineDay(
            range,
            TimelineRowsBuilder.PartsAround(_configuration.Current.DayPlan, span),
            // The other devices' first, so this PC's own sessions draw on top where they overlap.
            received.Concat(sessions).ToList(),
            drift,
            EventItems(range, settings),
            settings.ShowColdTurkey && _coldTurkey.Blocks is ColdTurkeyBlocks blocks ? BlockerTimeline.ColdTurkey(blocks, range) : null,
            focused,
            unfocused,
            taskMarks);

        (List<DayBarSpan> night, List<DayBarSpan> missed) = SleepSpans(now, range);
        IReadOnlyList<TimelineRowChoice> rows = settings.RowsInOrder();
        Layout = new DayBarLayout(
            TimelineRowsBuilder.Build(timelineDay, rows), Ticks(range), Fraction(now, range), night, missed, range.Start, range.End);
        UpdateSleepLine(now);
        HasPlannedFocus = planned.Any(focus => DayTimeline.Clip(focus.Window, range) is not null);

        DateTime date = day.Start.Date;
        DateText = $"{date.ToString("ddd", CultureInfo.CurrentCulture)} ({date:M/d})";
        StatusText = Status(now, range, planned);
        SummaryText = Summary(range, planned, focused, drift.Count);
        UpdateLegend(range, planned, focused, distracted, Total(unfocused, range), taskMarks.Count, rows);
    }

    /// <summary>Time on distracting sites inside this PC's sessions (each visit's own time, overlaps once).</summary>
    private TimeSpan DistractedTime(IReadOnlyList<TimelineSession> sessions, LocalWindow range)
    {
        var inSessions = new List<LocalWindow>();
        foreach (DriftStretch stretch in _drift)
        {
            var window = new LocalWindow(ToLocal(stretch.Start), ToLocal(stretch.End));
            inSessions.AddRange(sessions.Select(session => DayTimeline.Clip(window, session.Window)).OfType<LocalWindow>());
        }

        return Total(inSessions, range);
    }

    /// <summary>Guided sessions' ticked tasks (ADR 24) in the range: "✓ Write intro · 25m" (since the last tick or the start).</summary>
    private IReadOnlyList<TimelineItem> TaskMarks(LocalWindow range)
    {
        if (_tasks is null)
        {
            return Array.Empty<TimelineItem>();
        }

        var marks = new List<TimelineItem>();
        foreach (SessionChecklist checklist in _tasks.History.Append(_tasks.Current).Where(item => item.SessionStart is not null))
        {
            DateTimeOffset previous = checklist.SessionStart!.Value;
            foreach (SessionTask task in checklist.Tasks.Where(task => task.DoneAt is not null).OrderBy(task => task.DoneAt))
            {
                DateTime at = ToLocal(task.DoneAt!.Value);
                if (at >= range.Start && at <= range.End)
                {
                    string took = TimelineRowsBuilder.Duration(task.DoneAt.Value - previous);
                    marks.Add(new TimelineItem(new LocalWindow(at, at), $"✓ {task.Title} · {took}", TimelineRowsBuilder.TaskMarkColor));
                }

                previous = task.DoneAt.Value;
            }
        }

        return marks;
    }

    // The legend under the bar, lined up with its row labels: planned work by kind, and the focus so far.
    private void UpdateLegend(LocalWindow range, IReadOnlyList<PlannedFocus> planned, TimeSpan focused, TimeSpan distracted,
        TimeSpan notInSession, int tasksDone, IReadOnlyList<TimelineRowChoice> rows)
    {
        List<PlanTotal> totals = DayTimeline.PlannedByName(planned, range)
            .Select(entry => new PlanTotal($"{entry.Name} {Duration(entry.Length)}", BrushOf(planned.First(focus => focus.Name == entry.Name).Color)))
            .ToList();
        // Redrawn every few seconds: only replace the list when a name, time or colour changed, so the legend isn't
        // rebuilt each time.
        static string KeyOf(PlanTotal total) => $"{total.Text}|{(total.Brush as ISolidColorBrush)?.Color}";
        if (!totals.Select(KeyOf).SequenceEqual(PlanTotals.Select(KeyOf)))
        {
            PlanTotals = totals;
        }

        ShowsPlanTotals = totals.Count > 0 && rows.Any(row => row.Row == TimelineRow.Plan && row.Shown);
        var focus = new List<PlanTotal> { new($"{Duration(focused)} focused", DayBarControl.FocusBrush) };
        if (distracted >= TimeSpan.FromMinutes(1))
        {
            focus.Add(new PlanTotal($"Distracted {Duration(distracted)}", DistractedBrush));
        }

        if (notInSession >= TimeSpan.FromMinutes(1))
        {
            focus.Add(new PlanTotal($"Not in a session {Duration(notInSession)}", NotInSessionBrush));
        }

        if (tasksDone > 0)
        {
            focus.Add(new PlanTotal(tasksDone == 1 ? "1 task done" : $"{tasksDone} tasks done", TaskBrush));
        }

        if (!focus.Select(KeyOf).SequenceEqual(FocusTotals.Select(KeyOf)))
        {
            FocusTotals = focus;
        }
        ShowsFocusTotal = rows.Any(row => row.Row == TimelineRow.Focus && row.Shown);
    }

    private static readonly IBrush DistractedBrush = new SolidColorBrush(Color.Parse("#EF4444"));
    private static readonly IBrush NotInSessionBrush = new SolidColorBrush(Color.Parse(TimelineRowsBuilder.NotInSessionColor));
    private static readonly IBrush TaskBrush = new SolidColorBrush(Color.Parse(TimelineRowsBuilder.TaskMarkColor));

    private static IBrush BrushOf(string color)
    {
        return Color.TryParse(color, out Color parsed) ? new SolidColorBrush(parsed) : DayBarControl.FocusBrush;
    }

    /// <summary>Omitted sessions and old scheduled blocks: their time (and drift) counts nowhere.</summary>
    private List<TimeRange> LeftOut()
    {
        DateTimeOffset now = _time.GetUtcNow();
        return _records
            .Where(record => _omitted.IsOmitted(record.StartedAt) || record.Mode == SessionMode.Scheduled)
            .Select(record => new TimeRange(record.StartedAt, record.EndedAt ?? now))
            .ToList();
    }

    /// <summary>One mark per visit to a distracting site (the ones inside the drawn sessions are kept by the caller).</summary>
    private List<TimelineItem> DriftItems()
    {
        var items = new List<TimelineItem>();
        foreach (DriftStretch mark in DriftMarks.Merge(_drift, DriftGap))
        {
            var window = new LocalWindow(ToLocal(mark.Start), ToLocal(mark.End));
            items.Add(new TimelineItem(window, TimelineRowsBuilder.DriftTip(mark.Site, window), null));
        }

        return items;
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
        SleepSchedule schedule = _sleep.Schedule(_configuration.Current.DayPlan);
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

        SleepSchedule schedule = _sleep.Schedule(_configuration.Current.DayPlan);
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

    // From when bed now would give the sleep goal until wake time: the sleep left counts down, with the wake time.
    private void UpdateSleepLine(DateTime now)
    {
        SleepSchedule schedule = _sleep.Schedule(_configuration.Current.DayPlan);
        LocalWindow night = schedule.NightAround(now);
        TimeSpan goal = _sleep.Current.Goal;
        ShowsSleep = _sleep.Current.Enabled && SleepSchedule.ShowsCountdown(now, night, goal);
        if (!ShowsSleep)
        {
            return;
        }

        TimeSpan sleep = SleepSchedule.SleepIfBedNow(now, night);
        SleepAmount = SleepSchedule.Rate(sleep, goal);
        SleepBrush = SleepAmount switch
        {
            SleepAmount.Good => GoodSleepBrush,
            SleepAmount.Okay => OkaySleepBrush,
            _ => ShortSleepBrush,
        };
        DateOnly evening = DateOnly.FromDateTime(night.End).AddDays(-1);
        SleepText = $"Sleep if you go now: {Duration(sleep)} · up {night.End:t}" + (schedule.WakesByAlarm(evening) ? " (phone alarm)" : "");
    }

    private List<(LocalWindow Window, string Tip, Guid? ProfileId, bool Added)> ActualSessions(DateTime now, LocalWindow span)
    {
        var sessions = new List<(LocalWindow Window, string Tip, Guid? ProfileId, bool Added)>();
        SessionState state = _sessions.State;
        bool runningShown = false;
        // The session running now is the newest one the app recorded (an added one never runs).
        int newestRecorded = -1;
        for (int i = 0; i < _records.Count && newestRecorded < 0; i++)
        {
            newestRecorded = _records[i].Mode == SessionMode.Added ? -1 : i;
        }

        for (int i = 0; i < _records.Count; i++)
        {
            SessionRecord record = _records[i];
            if (_omitted.IsOmitted(record.StartedAt))
            {
                // Omitted (e.g. a test): it doesn't count (the running one is also left out).
                runningShown |= i == newestRecorded && state.IsActive && record.EndedAt is null;
                continue;
            }

            if (record.Mode == SessionMode.Scheduled)
            {
                // A scheduled block blocks, but only sessions you start count as focus.
                runningShown |= i == newestRecorded && state.IsActive && record.EndedAt is null;
                continue;
            }

            DateTime start = ToLocal(record.StartedAt);
            string profile = ProfileName(record.ProfileId);

            if (record.Mode == SessionMode.Added && record.EndedAt is DateTimeOffset addedEnd)
            {
                // Added by hand in History (ADR 20): counted like the others, drawn striped, no profile to open.
                var window = new LocalWindow(start, ToLocal(addedEnd));
                string note = record.Reason is null ? string.Empty : $" · “{record.Reason}”";
                sessions.Add((window, $"Added by hand · {Times(window)}{note}", null, true));
            }
            else if (record.EndedAt is DateTimeOffset ended)
            {
                var window = new LocalWindow(start, ToLocal(ended));
                sessions.Add((window, $"{profile} · ran {Times(window)}", record.ProfileId, false));
            }
            else if (i == newestRecorded && state.IsActive)
            {
                // The newest session without an end is the one running now.
                sessions.Add((new LocalWindow(start, now), $"{profile} · running since {start:t}", record.ProfileId, false));
                runningShown = true;
            }

            // Older sessions without an end stopped when the app did; when isn't known, so they're left out.
        }

        if (state.IsActive && !runningShown && state.Mode != SessionMode.Scheduled)
        {
            // Not in the history (history off, or not written yet): show the current phase.
            DateTime start = state.PhaseStartedAt is DateTimeOffset phaseStart ? ToLocal(phaseStart) : now;
            sessions.Add((new LocalWindow(start, now), $"{ProfileName(state.ProfileId)} · running since {start:t}", state.ProfileId, false));
        }

        return sessions.Where(item => DayTimeline.Clip(item.Window, span) is not null).ToList();
    }

    /// <summary>Events of the visible calendars chosen for the timeline, in their calendar's colour.</summary>
    private List<TimelineItem> EventItems(LocalWindow range, DayBarSettings settings)
    {
        var items = new List<TimelineItem>();
        var seen = new HashSet<(string, string)>();
        for (DateTime date = range.Start.Date; date < range.End; date = date.AddDays(1))
        {
            foreach (CalendarEntry entry in _calendars.EventsOn(DateOnly.FromDateTime(date)))
            {
                CalendarEvent calendarEvent = entry.Event;
                if (!settings.ShowsCalendar(entry.Calendar.Id) || !seen.Add((calendarEvent.CalendarId, calendarEvent.Id)))
                {
                    continue;
                }

                var window = new LocalWindow(calendarEvent.LocalStart, calendarEvent.LocalEnd > calendarEvent.LocalStart
                    ? calendarEvent.LocalEnd
                    : calendarEvent.LocalStart.AddMinutes(1));
                string when = calendarEvent.IsAllDay ? "all day" : Times(window);
                items.Add(new TimelineItem(window, $"{calendarEvent.Title} · {when}", entry.Calendar.Color));
            }
        }

        return items;
    }

    private string Status(DateTime now, LocalWindow range, IReadOnlyList<PlannedFocus> planned)
    {
        SessionState state = _sessions.State;
        if (state.IsActive)
        {
            string profile = ProfileName(state.ProfileId);
            string? until = state.PhaseEndsAt is DateTimeOffset end ? $"{ToLocal(end):t}" : null;
            if (state.Mode == SessionMode.Scheduled)
            {
                return until is null ? $"Blocks on · {profile}" : $"Blocks on · {profile} until {until}";
            }

            return state.IsEnforcing
                ? until is null ? $"In focus · {profile}, no set end" : $"In focus · {profile} until {until}"
                : until is null ? "On a break" : $"On a break until {until}";
        }

        PlannedFocus? current = planned.FirstOrDefault(focus => focus.Window.Contains(now));
        if (current is not null)
        {
            return $"{current.Name} until {current.Window.End:t} · no focus session running";
        }

        PlannedFocus? next = planned.FirstOrDefault(focus => focus.Window.Start > now && focus.Window.Start < range.End);
        if (next is not null)
        {
            return $"Next: {next.Name} at {next.Window.Start:t}";
        }

        return planned.Any(focus => DayTimeline.Clip(focus.Window, range) is not null)
            ? "No more work planned today"
            : "No work planned today";
    }

    /// <summary>E.g. "2h 55m focused · 2 drifts · 9h 00m of work planned"; the breakdown by kind is under the bar.</summary>
    private static string Summary(LocalWindow range, IReadOnlyList<PlannedFocus> planned, TimeSpan focused, int drifts)
    {
        TimeSpan plannedTotal = Total(planned.Select(focus => focus.Window), range);
        string text = $"{Duration(focused)} focused";
        if (drifts > 0)
        {
            text += drifts == 1 ? " · 1 drift" : $" · {drifts} drifts";
        }

        if (plannedTotal <= TimeSpan.Zero)
        {
            return text;
        }

        return text + $" · {Duration(plannedTotal)} of work planned";
    }

    /// <summary>The stretches the user stepped away during this PC's sessions; one still going runs until now.</summary>
    private List<LocalWindow> AwayWindows(DateTime now)
    {
        var away = new List<LocalWindow>();
        foreach (SessionRecord record in _records)
        {
            foreach (TimeRange stretch in record.Away ?? [])
            {
                DateTime end = stretch.To == DateTimeOffset.MaxValue ? now : ToLocal(stretch.To);
                if (end > ToLocal(stretch.From))
                {
                    away.Add(new LocalWindow(ToLocal(stretch.From), end));
                }
            }
        }

        return away;
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

    /// <summary>The other devices' sessions on the day shown, in their shade, e.g. "Work PC · Deep work · 9:00 AM – 10:30 AM".</summary>
    private List<TimelineSession> ReceivedOnBar(LocalWindow span)
    {
        if (_received is null)
        {
            return [];
        }

        TimeZoneInfo zone = _time.LocalTimeZone;
        DateTimeOffset from = new(span.Start, zone.GetUtcOffset(span.Start));
        DateTimeOffset to = new(span.End, zone.GetUtcOffset(span.End));
        return ReceivedSessions.In(_received.Devices, from, to)
            .Select(session =>
            {
                var window = new LocalWindow(ToLocal(session.Start), ToLocal(session.End));
                string what = session.Added ? "added by hand" : session.Profile ?? "focus";
                return new TimelineSession(window, $"{session.DeviceName} · {what} · {Times(window)}", null, Color: ReceivedSessions.ColorOf(session.DeviceId));
            })
            .ToList();
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
