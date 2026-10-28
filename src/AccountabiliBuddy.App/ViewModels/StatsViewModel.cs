using System.Collections.ObjectModel;
using System.Globalization;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.Services.Sync;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The Stats page (design 5p, 19a to 19c): a view (Overview, Consistency, Intensity, Focus graph, Drift, Sleep, Phone) over a
/// range (Day, Week, Month, Year), and the selected day with its strip, tiles, list and "Day details" (History, unchanged). It
/// shows only what History shows and what the phone already syncs, added up differently: counted focus across the user's
/// devices, drift from the activity log, the phone's pickups during sessions and its smart bedtime's numbers. A view with no
/// data says so (it never draws zeros that weren't measured); Sleep and Phone hide with no phone. The chosen view and range are
/// kept in the document "stats.view". Reads again when the history, the goal, the plan or the phone's file changes and when the
/// page is shown; no timers of its own beyond coalescing a burst of log writes.
/// </summary>
public sealed partial class StatsViewModel : ObservableObject
{
    private const string DocumentKey = "stats.view";
    private const int KeptDays = 90;
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RunningRefreshDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PickupStartTolerance = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DriftGap = TimeSpan.FromMinutes(2);

    private readonly IActivityLog _log;
    private readonly Func<DateTimeOffset, DateTimeOffset, Task<IReadOnlyList<SessionRecord>>> _loadRecords;
    private readonly ReceivedDevicesService _received;
    private readonly OmittedSessionsService _omitted;
    private readonly DeletedUsageService _deleted;
    private readonly DisplayNamesService _names;
    private readonly GoalService _goal;
    private readonly ConfigurationService _configuration;
    private readonly SleepSettingsService _sleep;
    private readonly PhonePickups _phone;
    private readonly PrivacySettingsService _privacy;
    private readonly SqliteJsonDocumentStore _documents;
    private readonly TimeProvider _time;
    private readonly Func<bool> _isSessionRunning;
    private readonly DispatcherTimer _refreshSoon;
    private StatsData? _data;
    private int _refreshNumber;
    private bool _isShown;
    private bool _tabSelected;
    private bool _windowVisible = true;

    /// <param name="loadRecords">This PC's sessions between two times, newest first, with marked distracted stretches applied.</param>
    public StatsViewModel(
        HistoryViewModel details,
        IActivityLog log,
        ActivityRecorder recorder,
        Func<DateTimeOffset, DateTimeOffset, Task<IReadOnlyList<SessionRecord>>> loadRecords,
        ReceivedDevicesService received,
        OmittedSessionsService omitted,
        DeletedUsageService deleted,
        DisplayNamesService names,
        GoalService goal,
        ConfigurationService configuration,
        SleepSettingsService sleep,
        PhonePickups phone,
        PrivacySettingsService privacy,
        SqliteJsonDocumentStore documents,
        TimeProvider time,
        Func<bool> isSessionRunning)
    {
        Details = details;
        _log = log;
        _loadRecords = loadRecords;
        _received = received;
        _omitted = omitted;
        _deleted = deleted;
        _names = names;
        _goal = goal;
        _configuration = configuration;
        _sleep = sleep;
        _phone = phone;
        _privacy = privacy;
        _documents = documents;
        _time = time;
        _isSessionRunning = isSessionRunning;
        SelectedDay = Today;
        // The fold starts on the same day as the column.
        details.SelectedDay = SelectedDay.ToDateTime(TimeOnly.MinValue);

        foreach ((StatsViewKind kind, string text) in new[]
                 {
                     (StatsViewKind.Overview, "Overview"), (StatsViewKind.Consistency, "Consistency"), (StatsViewKind.Intensity, "Intensity"),
                     (StatsViewKind.FocusGraph, "Focus graph"), (StatsViewKind.Drift, "Drift"), (StatsViewKind.Sleep, "Sleep"), (StatsViewKind.Phone, "Phone"),
                 })
        {
            Chips.Add(new StatsChipViewModel(kind, text));
        }

        foreach ((StatsRange range, string text) in new[] { (StatsRange.Day, "Day"), (StatsRange.Week, "Week"), (StatsRange.Month, "Month"), (StatsRange.Year, "Year") })
        {
            RangeOptions.Add(new StatsRangeOptionViewModel(range, text));
        }

        LoadPreferences();
        ShowPreferences();

        // A burst of log writes becomes one read.
        _refreshSoon = new DispatcherTimer { Interval = RefreshDelay };
        _refreshSoon.Tick += (_, _) =>
        {
            _refreshSoon.Stop();
            RequestRefresh();
        };
        recorder.LogChanged += (_, _) => StartRefreshTimer();
        privacy.Changed += (_, _) => RequestRefresh();
        received.Changed += (_, _) => RequestRefresh();
        omitted.Changed += (_, _) => RequestRefresh();
        deleted.Changed += (_, _) => RequestRefresh();
        goal.Changed += (_, _) => RequestRefresh();
        configuration.Changed += (_, _) => RequestRefresh();
        sleep.Changed += (_, _) => RequestRefresh();
        sleep.PhoneAlarmChanged += (_, _) => RequestRefresh();
        details.AddedSessionsChanged += (_, _) => RequestRefresh();
        // A form of the Day details opening (e.g. from a click on the day bar) opens the fold that holds it.
        details.PropertyChanged += (_, e) =>
        {
            // The fold changed its own day (saving an added session on another day shows that day): the column follows.
            if (e.PropertyName == nameof(HistoryViewModel.SelectedDay) && details.SelectedDay is DateTime shown
                && DateOnly.FromDateTime(shown) != SelectedDay && DateOnly.FromDateTime(shown) <= Today)
            {
                SelectedDay = DateOnly.FromDateTime(shown);
                RebuildDay();
                if (Range == StatsRange.Day)
                {
                    RebuildView();
                }
            }

            if ((e.PropertyName == nameof(HistoryViewModel.IsAddFormOpen) && details.IsAddFormOpen)
                || (e.PropertyName == nameof(HistoryViewModel.IsGapFormOpen) && details.IsGapFormOpen))
            {
                ShowDetails();
            }
        };
    }

    /// <summary>History's own lists and forms, shown in the "Day details" fold.</summary>
    public HistoryViewModel Details { get; }

    public ObservableCollection<StatsChipViewModel> Chips { get; } = new();

    public ObservableCollection<StatsRangeOptionViewModel> RangeOptions { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsOverview), nameof(ShowsConsistency), nameof(ShowsIntensity), nameof(ShowsFocusGraph),
        nameof(ShowsDrift), nameof(ShowsSleep), nameof(ShowsPhone), nameof(ShowsEmpty), nameof(EmptyText), nameof(Subtitle), nameof(ShowsDayColumn), nameof(ShowsRange))]
    public partial StatsViewKind View { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    public partial StatsRange Range { get; set; } = StatsRange.Week;

    /// <summary>The day shown in the right column.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DayTitle), nameof(CanGoNext), nameof(CanGoPrevious))]
    public partial DateOnly SelectedDay { get; set; }

    /// <summary>The "Day details" fold is open.</summary>
    [ObservableProperty]
    public partial bool IsDetailsOpen { get; set; }

    /// <summary>The first read is done.</summary>
    [ObservableProperty]
    public partial bool IsLoaded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsOverview), nameof(ShowsEmpty))]
    public partial StatsBarPanel? Overview { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsConsistency), nameof(ShowsEmpty))]
    public partial StatsHeatPanel? Consistency { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsIntensity), nameof(ShowsEmpty))]
    public partial StatsHeatPanel? Intensity { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsFocusGraph), nameof(ShowsEmpty))]
    public partial StatsTrendPanel? Trend { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsDrift), nameof(ShowsEmpty))]
    public partial StatsBarPanel? DriftPanel { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsSleep), nameof(ShowsEmpty))]
    public partial StatsBarPanel? SleepPanel { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsPhone), nameof(ShowsEmpty))]
    public partial StatsBarPanel? PhonePanel { get; set; }

    [ObservableProperty]
    public partial StatsDayPanel? Day { get; set; }

    /// <summary>"Oct 26 – Nov 1", or the day for the Day range.</summary>
    [ObservableProperty]
    public partial string RangeText { get; set; } = string.Empty;

    /// <summary>The 330 column (the selected day and the Day details) beside the views that read day by day; the others use the full width (19a to 19c).</summary>
    public bool ShowsDayColumn => View is StatsViewKind.Overview or StatsViewKind.Drift or StatsViewKind.Phone;

    /// <summary>The range segment shows with the views that have ranges (Sleep's numbers are the phone's last two weeks).</summary>
    public bool ShowsRange => AllowedRanges(View).Count > 0;

    public bool ShowsOverview => View == StatsViewKind.Overview && Overview is not null;

    public bool ShowsConsistency => View == StatsViewKind.Consistency && Consistency is not null;

    public bool ShowsIntensity => View == StatsViewKind.Intensity && Intensity is not null;

    public bool ShowsFocusGraph => View == StatsViewKind.FocusGraph && Trend is not null;

    public bool ShowsDrift => View == StatsViewKind.Drift && DriftPanel is not null;

    public bool ShowsSleep => View == StatsViewKind.Sleep && SleepPanel is not null;

    public bool ShowsPhone => View == StatsViewKind.Phone && PhonePanel is not null;

    /// <summary>The shown view has nothing in this range: the page says so instead of drawing it.</summary>
    public bool ShowsEmpty => IsLoaded && !(ShowsOverview || ShowsConsistency || ShowsIntensity || ShowsFocusGraph || ShowsDrift || ShowsSleep || ShowsPhone);

    public string EmptyText => View switch
    {
        StatsViewKind.Sleep => "Your phone hasn't sent sleep numbers yet.",
        StatsViewKind.Phone => "No sessions with a phone count in this range.",
        StatsViewKind.Drift => _data is { LoggingOn: false } ? "Activity history is off, so drift isn't being recorded." : "No sessions in this range yet.",
        _ => "No focus sessions in this range yet. Start one on Today and this fills in.",
    };

    /// <summary>The Muted line under "Stats".</summary>
    public string Subtitle => View switch
    {
        StatsViewKind.Consistency => "Days you hit your deep work goal",
        StatsViewKind.Intensity => $"When your best focus happens · last {IntensityWeeks()} weeks",
        StatsViewKind.FocusGraph => Range == StatsRange.Week
            ? "Deep work and drift, day by day · last 14 days"
            : "Deep work and drift, daily average per week · last 12 weeks",
        StatsViewKind.Drift => $"Time off track in your sessions · {RangeText}",
        StatsViewKind.Sleep => "From your phone's smart bedtime · last 2 weeks",
        StatsViewKind.Phone => $"Pickups during sessions · {RangeText}",
        _ => RangeText,
    };

    public string DayTitle => SelectedDay.ToString("dddd, MMM d", CultureInfo.CurrentCulture);

    public bool CanGoPrevious => _data is null || SelectedDay > _data.FirstDay;

    public bool CanGoNext => SelectedDay < Today;

    private DateOnly Today => DateOnly.FromDateTime(_time.GetLocalNow().DateTime);

    /// <summary>The Stats tab was selected or left.</summary>
    public void SetTabSelected(bool selected)
    {
        _tabSelected = selected;
        UpdateShown();
    }

    /// <summary>The main window was shown or hidden to the tray (the day bar pauses the same way).</summary>
    public void SetWindowVisible(bool visible)
    {
        _windowVisible = visible;
        UpdateShown();
    }

    // Nothing is read while the page can't be seen; showing it reads again, so a session running now is in it.
    private void UpdateShown()
    {
        bool shown = _tabSelected && _windowVisible;
        if (shown == _isShown)
        {
            return;
        }

        _isShown = shown;
        if (shown)
        {
            _ = RefreshAsync();
        }
        else
        {
            _refreshSoon.Stop();
        }
    }

    // A change while the page is hidden is read when it is shown.
    private void RequestRefresh()
    {
        if (_isShown)
        {
            _ = RefreshAsync();
        }
    }

    // A burst of log writes becomes one read, spaced out while a session runs (the log changes about every few seconds then).
    private void StartRefreshTimer()
    {
        if (!_isShown)
        {
            return;
        }

        _refreshSoon.Interval = _isSessionRunning() ? RunningRefreshDelay : RefreshDelay;
        _refreshSoon.Start();
    }

    /// <summary>
    /// Opens the Day details fold. The fold lives in the right column, which the full-width views leave out, so a view
    /// without it becomes Overview first.
    /// </summary>
    public void ShowDetails()
    {
        if (!ShowsDayColumn)
        {
            View = StatsViewKind.Overview;
            CoerceRange();
            ShowPreferences();
            RebuildView();
            SavePreferences();
        }

        IsDetailsOpen = true;
    }

    [RelayCommand]
    private void SelectView(StatsViewKind kind)
    {
        if (kind == View)
        {
            return;
        }

        View = kind;
        CoerceRange();
        ShowPreferences();
        RebuildView();
        SavePreferences();
    }

    [RelayCommand]
    private void SelectRange(StatsRange range)
    {
        if (range == Range || !AllowedRanges(View).Contains(range))
        {
            return;
        }

        Range = range;
        ShowPreferences();
        RebuildView();
        SavePreferences();
    }

    /// <summary>Shows a day in the right column and in the Day details (a click on a bar or a cell does this).</summary>
    [RelayCommand]
    private void SelectDay(DateOnly date)
    {
        DateOnly first = _data?.FirstDay ?? Today.AddDays(1 - KeptDays);
        DateOnly day = date > Today ? Today : date < first ? first : date;
        bool same = day == SelectedDay;
        SelectedDay = day;
        Details.SelectedDay = day.ToDateTime(TimeOnly.MinValue);
        // The fold shows this day too, even if it was on "Last 7 days".
        if (Details.Period != HistoryPeriod.Day)
        {
            Details.Period = HistoryPeriod.Day;
        }

        if (same)
        {
            return;
        }

        RebuildDay();
        if (Range == StatsRange.Day)
        {
            RebuildView();
        }
    }

    /// <summary>Opens a day of a heatmap: its hours on Overview, with the day in the right column.</summary>
    [RelayCommand]
    private void OpenDay(DateOnly date)
    {
        View = StatsViewKind.Overview;
        Range = StatsRange.Day;
        SelectDay(date);
        ShowPreferences();
        RebuildView();
        SavePreferences();
    }

    [RelayCommand]
    private void PreviousDay() => SelectDay(SelectedDay.AddDays(-1));

    [RelayCommand]
    private void NextDay() => SelectDay(SelectedDay.AddDays(1));

    [RelayCommand]
    private void ToggleDetails()
    {
        IsDetailsOpen = !IsDetailsOpen;
    }

    /// <summary>Refreshes the page's data, then everything built from it.</summary>
    public async Task RefreshAsync()
    {
        int number = ++_refreshNumber;
        DateTimeOffset now = _time.GetUtcNow();
        TimeZoneInfo zone = _time.LocalTimeZone;
        DateTime nowLocal = TimeZoneInfo.ConvertTime(now, zone).DateTime;
        DateOnly today = DateOnly.FromDateTime(nowLocal);
        DateOnly first = today.AddDays(1 - KeptDays);
        DateTime startLocal = first.ToDateTime(TimeOnly.MinValue);
        var from = new DateTimeOffset(startLocal, zone.GetUtcOffset(startLocal));
        IReadOnlyList<SessionRecord> records;
        IReadOnlyList<DriftStretch> drift;
        try
        {
            records = await _loadRecords(from.AddDays(-1), now.AddDays(1));
            List<TimeRange> leftOut = records
                .Where(record => _omitted.IsOmitted(record.StartedAt) || record.Mode == SessionMode.Scheduled)
                .Select(record => new TimeRange(record.StartedAt, record.EndedAt ?? now))
                .ToList();
            drift = await _log.GetDriftAsync(from, now.AddDays(1), leftOut, _deleted.Parts);
        }
        catch (SqliteException)
        {
            return; // Keep what was shown.
        }

        if (number != _refreshNumber)
        {
            return; // A newer read is on its way.
        }

        // The services are read here on the UI thread; the union, day sums and merging of 90 days run off it.
        IReadOnlyList<SyncSession> others = ReceivedSessions.Closed(_received.Devices).ToList();
        HashSet<DateTimeOffset> omittedStarts = records.Where(record => _omitted.IsOmitted(record.StartedAt)).Select(record => record.StartedAt).ToHashSet();
        bool running = _isSessionRunning();
        // A session left out of the totals leaves its pickups out too.
        List<DateTimeOffset> leftOutStarts = records
            .Where(record => omittedStarts.Contains(record.StartedAt) || record.Mode == SessionMode.Scheduled)
            .Select(record => record.StartedAt).ToList();
        IReadOnlyList<SyncPickup> pickups = _phone.Between(from, now.AddDays(1))
            .Where(pickup => !leftOutStarts.Any(start => (start - pickup.SessionStart).Duration() <= PickupStartTolerance)).ToList();
        IReadOnlyList<ReceivedSession> receivedSessions = ReceivedSessions.In(_received.Devices, from, now.AddDays(1));
        SyncSmartBedtime? bedtime = PhoneBedtime.Of(_received.Devices);
        bool phoneCounts = _phone.IsCounting;
        bool loggingOn = _privacy.Current.ActivityLogEnabled;
        TimeSpan goal = TimeSpan.FromMinutes(Math.Max(1, _goal.Current.DailyMinutes));
        IReadOnlyList<SessionRecord> kept = records;
        (IReadOnlyList<LocalWindow> windows, IReadOnlyList<DeepWorkDay> days, IReadOnlyList<DriftStretch> visits) = await Task.Run(() =>
        {
            IReadOnlyList<LocalWindow> focused = DeepWork.Windows(kept, others, omittedStarts.Contains, running, now, zone);
            return (focused, DeepWork.DaysIn(focused, first, today), DriftMarks.Merge(drift, DriftGap));
        });
        if (number != _refreshNumber)
        {
            return; // A newer read is on its way.
        }

        // The first date with any history: earlier dates are "no data", never missed days.
        IEnumerable<DateOnly> starts = windows.Select(window => DateOnly.FromDateTime(window.Start))
            .Concat(records.Where(record => record.Mode != SessionMode.Scheduled).Select(record => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(record.StartedAt, zone).DateTime)))
            .Concat(receivedSessions.Select(session => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(session.Start, zone).DateTime)));
        DateOnly firstData = starts.DefaultIfEmpty(today).Min();
        if (firstData < first)
        {
            firstData = first;
        }

        DateOnly? previousToday = _data?.Today;
        _data = new StatsData(
            today, first, firstData, loggingOn, zone, nowLocal, goal, windows, days, visits, phoneCounts, pickups, bedtime, records, receivedSessions);
        // Past midnight with the day on "today": the column moves to the new today.
        if (previousToday is { } before && today > before && SelectedDay == before)
        {
            SelectedDay = today;
            Details.SelectedDay = today.ToDateTime(TimeOnly.MinValue);
        }

        if (SelectedDay > today)
        {
            SelectedDay = today;
        }

        IsLoaded = true;
        OnPropertyChanged(nameof(CanGoPrevious));
        RebuildChips();
        RebuildView();
        RebuildDay();
    }

    private StatsLookups Lookups()
    {
        return new StatsLookups(
            id => ProfileOptions.NameOf(_configuration.Current.Profiles, id) ?? "Focus session",
            _names.NameFor,
            _omitted.IsOmitted,
            date => _configuration.Current.DayPlan.IsWorkday(date),
            SleepOn);
    }

    // The planned night before the date and the one that starts on it.
    private IReadOnlyList<LocalWindow> SleepOn(DateOnly date)
    {
        if (!_sleep.Current.Enabled)
        {
            return [];
        }

        SleepSchedule schedule = _sleep.Schedule(_configuration.Current.DayPlan);
        return [schedule.Night(date.AddDays(-1)), schedule.Night(date)];
    }

    // Sleep and Phone chips show only with something to show; a chosen view that went away falls back to Overview.
    private void RebuildChips()
    {
        if (_data is null)
        {
            return;
        }

        bool hasSleep = StatsBarPanels.Sleep(_data.Bedtime) is not null;
        foreach (StatsChipViewModel chip in Chips)
        {
            chip.IsShown = chip.Kind switch
            {
                StatsViewKind.Sleep => hasSleep,
                StatsViewKind.Phone => _data.PhoneCounts,
                _ => true,
            };
        }

        if (!Chips.First(chip => chip.Kind == View).IsShown)
        {
            View = StatsViewKind.Overview;
            CoerceRange();
            ShowPreferences();
        }
    }

    // Builds the shown view only; the others are built when they are chosen.
    private void RebuildView()
    {
        if (_data is null)
        {
            return;
        }

        // The empty text depends on the data (e.g. activity history turned off), not only on the view.
        OnPropertyChanged(nameof(EmptyText));
        StatsLookups lookups = Lookups();
        (DateOnly from, DateOnly to) = StatsFormat.Bounds(Range, _data.Today);
        RangeText = Range == StatsRange.Day ? SelectedDay.ToString("ddd, MMM d", CultureInfo.CurrentCulture) : StatsFormat.DatesText(from, to);
        switch (View)
        {
            case StatsViewKind.Overview:
                Overview = StatsBarPanels.Overview(_data, lookups, Range, SelectedDay);
                break;
            case StatsViewKind.Consistency:
                Consistency = StatsHeatPanels.Consistency(_data, lookups, Range);
                break;
            case StatsViewKind.Intensity:
                Intensity = StatsHeatPanels.Intensity(_data, Range);
                break;
            case StatsViewKind.FocusGraph:
                Trend = StatsTrendPanels.Trend(_data, lookups, Range);
                break;
            case StatsViewKind.Drift:
                DriftPanel = StatsBarPanels.Drift(_data, lookups, Range);
                break;
            case StatsViewKind.Sleep:
                SleepPanel = StatsBarPanels.SleepView(_data);
                break;
            case StatsViewKind.Phone:
                PhonePanel = StatsBarPanels.Phone(_data, Range);
                break;
        }

        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(ShowsEmpty));
    }

    private void RebuildDay()
    {
        if (_data is not null)
        {
            Day = StatsDayBuilder.Build(_data, Lookups(), SelectedDay);
        }
    }

    private int IntensityWeeks()
    {
        return _data is null ? 8 : StatsHeatPanels.IntensityWeeks(_data, Range);
    }

    private static IReadOnlyList<StatsRange> AllowedRanges(StatsViewKind view) => view switch
    {
        StatsViewKind.Overview => [StatsRange.Day, StatsRange.Week, StatsRange.Month],
        StatsViewKind.Consistency or StatsViewKind.Intensity => [StatsRange.Week, StatsRange.Month, StatsRange.Year],
        StatsViewKind.FocusGraph or StatsViewKind.Drift or StatsViewKind.Phone => [StatsRange.Week, StatsRange.Month],
        _ => [],
    };

    // A range the new view doesn't have becomes the nearest one it does.
    private void CoerceRange()
    {
        IReadOnlyList<StatsRange> allowed = AllowedRanges(View);
        if (allowed.Count == 0 || allowed.Contains(Range))
        {
            return;
        }

        Range = Range == StatsRange.Day ? StatsRange.Week : allowed.Contains(StatsRange.Month) ? StatsRange.Month : allowed[0];
    }

    // The chips' and segments' selected and shown state follow View and Range.
    private void ShowPreferences()
    {
        foreach (StatsChipViewModel chip in Chips)
        {
            chip.IsSelected = chip.Kind == View;
        }

        IReadOnlyList<StatsRange> allowed = AllowedRanges(View);
        foreach (StatsRangeOptionViewModel option in RangeOptions)
        {
            option.IsShown = allowed.Contains(option.Range);
            option.IsSelected = option.Range == Range;
        }
    }

    private void LoadPreferences()
    {
        try
        {
            Preferences? saved = Task.Run(() => _documents.LoadAsync<Preferences>(DocumentKey)).GetAwaiter().GetResult();
            if (saved is not null && Enum.TryParse(saved.View, out StatsViewKind view) && Enum.TryParse(saved.Range, out StatsRange range))
            {
                View = view;
                Range = range;
                CoerceRange();
            }
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            // The defaults stay.
        }
    }

    private void SavePreferences()
    {
        _ = SaveAsync();
    }

    private async Task SaveAsync()
    {
        try
        {
            await _documents.SaveAsync(DocumentKey, new Preferences(View.ToString(), Range.ToString()));
        }
        catch (SqliteException)
        {
            // Not remembered this time; nothing else depends on it.
        }
    }

    private sealed record Preferences(string View, string Range);
}
