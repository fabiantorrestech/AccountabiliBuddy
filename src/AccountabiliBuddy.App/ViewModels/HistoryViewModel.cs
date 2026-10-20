using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Ports;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// History tab: a summary of the local activity log for today, the last 7 days or any one day (stepping back
/// through the kept 90 days) — focus time, how much of it was
/// on track or distracted, the top distractions, the most-used apps and sites, and the sessions with how they ended.
/// A session can be omitted (e.g. a test): it stays listed, greyed, but its focus time doesn't count.
/// Apps and sites show their icon (or a letter badge), and each has a ⋯ menu: rename, hide from that list, or delete
/// its time from the period shown (restorable); names that read the same are added up.
/// Refreshes itself a few seconds after new entries are written (entries are written when an activity ends).
/// </summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    private const int TopCount = 5;
    private const int KeptDays = 90;
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromSeconds(3);

    private readonly IActivityLog _log;
    private readonly ConfigurationService _configuration;
    private readonly PrivacySettingsService _privacy;
    private readonly OmittedSessionsService _omitted;
    private readonly DisplayNamesService _names;
    private readonly AppAndSiteIcons _icons;
    private readonly HiddenUsageService _hidden;
    private readonly HiddenUsageService _hiddenDistractions;
    private readonly DeletedUsageService _deleted;
    private readonly ProgramPathsService? _programs;
    private readonly IDesktopShell? _shell;

    // The period shown, for deleting from it.
    private DateTimeOffset _from;
    private DateTimeOffset _to;

    // The browser's own pages (new tab, settings, extensions, favorites…) share one row under the browser.
    private const string BrowserPagesName = "Browser pages";
    private readonly TimeProvider _timeProvider;
    private readonly DispatcherTimer _refreshSoon;

    public HistoryViewModel(
        IActivityLog log,
        ActivityRecorder recorder,
        ConfigurationService configuration,
        PrivacySettingsService privacy,
        OmittedSessionsService omitted,
        DisplayNamesService names,
        AppAndSiteIcons icons,
        HiddenUsageService hidden,
        HiddenUsageService hiddenDistractions,
        DeletedUsageService deleted,
        TimeProvider timeProvider,
        ProgramPathsService? programs = null,
        IDesktopShell? shell = null)
    {
        _programs = programs;
        _shell = shell;
        _hidden = hidden;
        _hidden.Changed += (_, _) => _ = RefreshAsync();
        _hiddenDistractions = hiddenDistractions;
        _hiddenDistractions.Changed += (_, _) => _ = RefreshAsync();
        _deleted = deleted;
        _deleted.Changed += (_, _) => _ = RefreshAsync();
        HiddenOverall = new HiddenListViewModel(hidden);
        HiddenDistractions = new HiddenListViewModel(hiddenDistractions);
        _names = names;
        _icons = icons;
        _names.Changed += (_, _) => _ = RefreshAsync();
        _icons.Changed += (_, _) => RefreshIcons();
        _omitted = omitted;
        _omitted.Changed += (_, _) => _ = RefreshAsync();
        _log = log;
        _configuration = configuration;
        _privacy = privacy;
        _timeProvider = timeProvider;

        FocusTimeText = OnTrackText = DistractedText = UnknownText = PeriodLabel = string.Empty;

        // Coalesce bursts of writes into one refresh.
        _refreshSoon = new DispatcherTimer { Interval = RefreshDelay };
        _refreshSoon.Tick += async (_, _) =>
        {
            _refreshSoon.Stop();
            await RefreshAsync();
        };
        recorder.LogChanged += (_, _) => _refreshSoon.Start();
        privacy.Changed += (_, _) => IsLoggingOff = !privacy.Current.ActivityLogEnabled;
        IsLoggingOff = !privacy.Current.ActivityLogEnabled;

        _ = RefreshAsync();
    }

    // The full lists; the shown ones hold the top 5 unless "Show all" is on.
    private List<UsageRowViewModel> _allDistractions = new();
    private List<UsageRowViewModel> _allOverall = new();

    /// <summary>Today, the last 7 days, or the day in <see cref="SelectedDay"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowToday), nameof(ShowWeek), nameof(ShowDay))]
    public partial HistoryPeriod Period { get; set; }

    public bool ShowToday
    {
        get => Period == HistoryPeriod.Today;
        set
        {
            if (value)
            {
                Period = HistoryPeriod.Today;
            }
        }
    }

    public bool ShowWeek
    {
        get => Period == HistoryPeriod.Week;
        set
        {
            if (value)
            {
                Period = HistoryPeriod.Week;
            }
        }
    }

    public bool ShowDay
    {
        get => Period == HistoryPeriod.Day;
        set
        {
            if (value)
            {
                Period = HistoryPeriod.Day;
            }
        }
    }

    /// <summary>The day shown when <see cref="Period"/> is a single day (for the date picker).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousDayCommand), nameof(NextDayCommand))]
    public partial DateTime? SelectedDay { get; set; }

    /// <summary>Oldest day still kept.</summary>
    public DateTime FirstDay => Today.AddDays(-(KeptDays - 1));

    public DateTime LastDay => Today;

    private DateTime Today => _timeProvider.GetLocalNow().Date;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DistractionsToggleText))]
    public partial bool ShowAllDistractions { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverallToggleText))]
    public partial bool ShowAllOverall { get; set; }

    public bool HasMoreDistractions => _allDistractions.Count > TopCount;

    public bool HasMoreOverall => _allOverall.Count > TopCount;

    public string DistractionsToggleText => ShowAllDistractions ? "Show less" : $"Show all ({_allDistractions.Count})";

    public string OverallToggleText => ShowAllOverall ? "Show less" : $"Show all ({_allOverall.Count})";

    /// <summary>Apps and sites hidden from "Most used".</summary>
    public HiddenListViewModel HiddenOverall { get; }

    /// <summary>Apps and sites hidden from "Top distractions".</summary>
    public HiddenListViewModel HiddenDistractions { get; }

    /// <summary>Deletions that touch the period shown, to restore them.</summary>
    public ObservableCollection<DeletedRowViewModel> DeletedRows { get; } = new();

    public bool HasDeleted => DeletedRows.Count > 0;

    public string DeletedToggleText => ShowDeleted ? "Done" : $"{DeletedRows.Count} deleted";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeletedToggleText))]
    public partial bool ShowDeleted { get; set; }

    [RelayCommand]
    private void ToggleDeleted()
    {
        ShowDeleted = !ShowDeleted;
    }

    [RelayCommand]
    private Task RestoreAsync(DeletedRowViewModel row)
    {
        return _deleted.RestoreAsync(row.Deletion.Id);
    }

    [ObservableProperty]
    public partial string PeriodLabel { get; set; }

    [ObservableProperty]
    public partial string FocusTimeText { get; set; }

    [ObservableProperty]
    public partial string OnTrackText { get; set; }

    [ObservableProperty]
    public partial string DistractedText { get; set; }

    [ObservableProperty]
    public partial string UnknownText { get; set; }

    [ObservableProperty]
    public partial bool IsLoggingOff { get; set; }

    [ObservableProperty]
    public partial string? ErrorText { get; set; }

    [ObservableProperty]
    public partial bool HasNoDistractions { get; set; }

    [ObservableProperty]
    public partial bool HasNoUsage { get; set; }

    [ObservableProperty]
    public partial bool HasNoSessions { get; set; }

    public ObservableCollection<UsageRowViewModel> TopDistractions { get; } = new();

    public ObservableCollection<UsageRowViewModel> TopOverall { get; } = new();

    public ObservableCollection<SessionRowViewModel> Sessions { get; } = new();

    partial void OnPeriodChanged(HistoryPeriod value)
    {
        if (value == HistoryPeriod.Day && SelectedDay is null)
        {
            SelectedDay = Today;
            return; // Refreshes from OnSelectedDayChanged.
        }

        _ = RefreshAsync();
    }

    partial void OnSelectedDayChanged(DateTime? value)
    {
        if (value is DateTime day && Period != HistoryPeriod.Day)
        {
            Period = HistoryPeriod.Day; // Refreshes from OnPeriodChanged.
            return;
        }

        _ = RefreshAsync();
    }

    partial void OnShowAllDistractionsChanged(bool value)
    {
        Show(TopDistractions, _allDistractions, value);
    }

    partial void OnShowAllOverallChanged(bool value)
    {
        Show(TopOverall, _allOverall, value);
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void PreviousDay()
    {
        SelectedDay = (SelectedDay ?? Today).Date.AddDays(-1);
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void NextDay()
    {
        SelectedDay = (SelectedDay ?? Today).Date.AddDays(1);
    }

    private bool CanGoBack => (SelectedDay ?? Today).Date > FirstDay;

    private bool CanGoForward => (SelectedDay ?? Today).Date < Today;

    [RelayCommand]
    private void ToggleAllDistractions()
    {
        ShowAllDistractions = !ShowAllDistractions;
    }

    [RelayCommand]
    private void ToggleAllOverall()
    {
        ShowAllOverall = !ShowAllOverall;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        DateTimeOffset localNow = _timeProvider.GetLocalNow();
        var startOfToday = new DateTimeOffset(localNow.Date, localNow.Offset);
        DateTime day = (SelectedDay ?? localNow.Date).Date;
        DateTimeOffset startOfDay = new(day, _timeProvider.LocalTimeZone.GetUtcOffset(day));
        (DateTimeOffset from, DateTimeOffset to) = Period switch
        {
            HistoryPeriod.Week => (startOfToday.AddDays(-6), startOfToday.AddDays(1)),
            HistoryPeriod.Day => (startOfDay, startOfDay.AddDays(1)),
            _ => (startOfToday, startOfToday.AddDays(1)),
        };
        _from = from;
        _to = to;
        PeriodLabel = Period switch
        {
            HistoryPeriod.Week => $"{from:ddd d MMM} – today",
            HistoryPeriod.Day when day == localNow.Date => $"Today, {day:dddd d MMMM}",
            HistoryPeriod.Day => $"{day:dddd d MMMM}",
            _ => $"Today, {localNow:dddd d MMMM}",
        };

        UsageSummary summary;
        IReadOnlyList<SessionRecord> records;
        try
        {
            // A day earlier, so a session running into the period is found too; the list shows those started in it.
            IReadOnlyList<SessionRecord> around = SessionHistoryBuilder.Build(await _log.GetSessionEventsAsync(from.AddDays(-1), to));
            records = around.Where(record => record.StartedAt >= from).ToList();

            // Omitted sessions don't count, and scheduled blocks block but aren't focus: both are left out of the
            // focus totals and distractions.
            List<TimeRange> leftOut = around
                .Where(record => _omitted.IsOmitted(record.StartedAt) || record.Mode == SessionMode.Scheduled)
                .Select(record => new TimeRange(record.StartedAt, record.EndedAt ?? localNow))
                .ToList();
            summary = await _log.GetSummaryAsync(from, to, leftOut, _deleted.Parts);
            ErrorText = null;
        }
        catch (SqliteException ex)
        {
            ErrorText = $"The history could not be read ({ex.Message}).";
            return;
        }

        FocusTimeText = FormatDuration(summary.FocusTime);
        OnTrackText = $"{FormatDuration(summary.OnTrackTime)} ({Percent(summary.OnTrackTime, summary.FocusTime)})";
        DistractedText = $"{FormatDuration(summary.DistractedTime)} ({Percent(summary.DistractedTime, summary.FocusTime)})";
        UnknownText = FormatDuration(summary.UnknownTime);

        // Distractions are named by site, or by app when there was no site; the period's sites tell them apart.
        var siteNames = summary.AppsAndSites.Where(entry => entry.Site is not null).Select(entry => entry.Site!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _allDistractions = BuildRows(summary.TopDistractions, siteNames);
        _allOverall = BuildAppRows(summary.AppsAndSites);
        DeletedRows.Clear();
        foreach (DeletedUsage deletion in _deleted.Overlapping(from, to))
        {
            DeletedRows.Add(new DeletedRowViewModel(deletion));
        }

        if (DeletedRows.Count == 0)
        {
            ShowDeleted = false;
        }

        OnPropertyChanged(nameof(HasDeleted));
        OnPropertyChanged(nameof(DeletedToggleText));
        Show(TopDistractions, _allDistractions, ShowAllDistractions);
        Show(TopOverall, _allOverall, ShowAllOverall);
        OnPropertyChanged(nameof(HasMoreDistractions));
        OnPropertyChanged(nameof(HasMoreOverall));
        OnPropertyChanged(nameof(DistractionsToggleText));
        OnPropertyChanged(nameof(OverallToggleText));
        foreach (string name in _allDistractions.Concat(_allOverall).Where(row => !row.HasIcon).SelectMany(row => row.RealNames))
        {
            // Works when that app is running now; sites get theirs from the browser extension.
            _icons.RequestAppIcon(name);
        }
        HasNoDistractions = _allDistractions.Count == 0;
        HasNoUsage = _allOverall.Count == 0;

        Sessions.Clear();
        foreach (SessionRecord record in records)
        {
            Sessions.Add(ToRow(record, startOfToday));
        }

        HasNoSessions = Sessions.Count == 0;
    }

    private SessionRowViewModel ToRow(SessionRecord record, DateTimeOffset startOfToday)
    {
        DateTimeOffset startedLocal = record.StartedAt.ToLocalTime();
        string when = startedLocal >= startOfToday ? $"Today {startedLocal:t}" : $"{startedLocal:ddd} {startedLocal:t}";

        string profile = ProfileOptions.NameOf(_configuration.Current.Profiles, record.ProfileId) ?? "(deleted profile)";
        string mode = record.Mode switch
        {
            SessionMode.Pomodoro => "Pomodoro",
            SessionMode.Scheduled => "Scheduled block (not counted as focus)",
            _ => "Manual",
        };

        string length = record.Length is TimeSpan duration ? FormatDuration(duration) : "—";
        string outcome = record.Outcome switch
        {
            SessionEventKind.Completed => "Completed",
            SessionEventKind.EndedEarly => "Ended early",
            SessionEventKind.Ended => "Ended",
            _ => "Running, or the app was closed",
        };
        if (record.AppClosedFor > TimeSpan.Zero)
        {
            outcome += $" · app closed {FormatDuration(record.AppClosedFor)}, resumed";
        }

        bool omitted = _omitted.IsOmitted(record.StartedAt);
        if (omitted)
        {
            outcome = "Omitted · " + outcome;
        }

        return new SessionRowViewModel(when, $"{profile} · {mode}", length, outcome, record.Reason, record.StartedAt, omitted);
    }

    [RelayCommand]
    private async Task ToggleOmittedAsync(SessionRowViewModel row)
    {
        await _omitted.SetOmittedAsync(row.StartedAt, !row.IsOmitted);
    }

    /// <summary>
    /// One row per shown name, longest first, adding up entries that show the same name (e.g. "WindowsTerminal" and
    /// "windowsterminal", or two names the user renamed alike).
    /// </summary>
    /// <param name="siteNames">The sites in the period, to tell site entries from app entries.</param>
    private List<UsageRowViewModel> BuildRows(IReadOnlyList<UsageEntry> entries, IReadOnlySet<string> siteNames)
    {
        var rows = entries
            .Where(entry => !_hiddenDistractions.IsHidden(entry.Name))
            .GroupBy(entry => _names.NameFor(entry.Name), StringComparer.OrdinalIgnoreCase)
            .Select(group => (Name: group.Key, RealNames: group.Select(entry => entry.Name).ToList(),
                Time: group.Aggregate(TimeSpan.Zero, (sum, entry) => sum + entry.Time)))
            .OrderByDescending(row => row.Time)
            .ToList();

        double max = rows.Count > 0 ? Math.Max(1, rows[0].Time.TotalSeconds) : 1;
        return rows.Select(row => new UsageRowViewModel(
                row.RealNames,
                row.Name,
                FormatDuration(row.Time),
                row.Time.TotalSeconds / max,
                row.RealNames.Select(_icons.Find).FirstOrDefault(found => found is not null),
                RenameAsync,
                hide: shown => _hiddenDistractions.SetHiddenAsync(shown.RealNames, hidden: true),
                delete: () => DeleteAsync(row.Name, row.RealNames.Select(name => new UsageDeletion(null, name, _from, _to))),
                periodText: PeriodForQuestion,
                isDistraction: true,
                showFile: row.RealNames.Any(siteNames.Contains) ? null : ShowFileFor(row.RealNames),
                openSite: row.RealNames.Any(siteNames.Contains) ? OpenSiteFor(row.RealNames) : null))
            .ToList();
    }

    /// <summary>
    /// One row per app (apps that show the same name added up), most time first; a browser's sites go under it,
    /// each added up the same way, with bars relative to the browser's time.
    /// </summary>
    private List<UsageRowViewModel> BuildAppRows(IReadOnlyList<AppSiteUsage> usage)
    {
        var apps = usage
            .Where(entry => !_hidden.IsHidden(entry.App))
            .GroupBy(entry => _names.NameFor(entry.App), StringComparer.OrdinalIgnoreCase)
            .Select(app => (Name: app.Key,
                RealNames: app.Select(entry => entry.App).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Time: app.Aggregate(TimeSpan.Zero, (sum, entry) => sum + entry.Time),
                Sites: app.Where(entry => entry.Site is not null).ToList()))
            .OrderByDescending(app => app.Time)
            .ToList();

        double max = apps.Count > 0 ? Math.Max(1, apps[0].Time.TotalSeconds) : 1;
        return apps.Select(app =>
            {
                double appSeconds = Math.Max(1, app.Time.TotalSeconds);
                List<UsageRowViewModel> sites = app.Sites
                    .Where(entry => !_hidden.IsHidden(entry.Site!))
                    .GroupBy(entry => IsBrowserPage(entry.Site!) ? BrowserPagesName : _names.NameFor(entry.Site!), StringComparer.OrdinalIgnoreCase)
                    .Select(site => (Name: site.Key,
                        RealNames: site.Select(entry => entry.Site!).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                        Time: site.Aggregate(TimeSpan.Zero, (sum, entry) => sum + entry.Time)))
                    .OrderByDescending(site => site.Time)
                    .Select(site => new UsageRowViewModel(
                        site.RealNames, site.Name, FormatDuration(site.Time), site.Time.TotalSeconds / appSeconds, FindIcon(site.RealNames), RenameAsync,
                        hide: HideAsync,
                        delete: () => DeleteAsync(
                            $"{site.Name} in {app.Name}",
                            app.RealNames.SelectMany(appName => site.RealNames.Select(siteName => new UsageDeletion(appName, siteName, _from, _to)))),
                        periodText: PeriodForQuestion,
                        openSite: OpenSiteFor(site.RealNames)))
                    .ToList();
                return new UsageRowViewModel(
                    app.RealNames, app.Name, FormatDuration(app.Time), app.Time.TotalSeconds / max, FindIcon(app.RealNames), RenameAsync, sites,
                    HideAsync,
                    delete: () => DeleteAsync(app.Name, app.RealNames.Select(appName => new UsageDeletion(appName, null, _from, _to))),
                    periodText: PeriodForQuestion,
                    showFile: ShowFileFor(app.RealNames));
            })
            .ToList();
    }

    /// <summary>Shows the first of these apps whose program file can be found in File Explorer; false when none can.</summary>
    private Func<Task<bool>>? ShowFileFor(IReadOnlyList<string> processNames)
    {
        if (_programs is null || _shell is null)
        {
            return null;
        }

        return async () =>
        {
            foreach (string processName in processNames)
            {
                if (await _programs.FindAsync(processName) is string path)
                {
                    _shell.ShowInFileManager(path);
                    return true;
                }
            }

            return false;
        };
    }

    /// <summary>Opens the first real website among these names in the browser; null when there is none (apps, browser pages).</summary>
    private Action? OpenSiteFor(IReadOnlyList<string> names)
    {
        string? site = names.FirstOrDefault(IsWebsite);
        return site is null || _shell is null ? null : () => _shell.OpenWebsite(site);
    }

    /// <summary>A website's host: has a dot, isn't a browser page, and is a valid host name.</summary>
    private static bool IsWebsite(string name)
    {
        return name.Contains('.') && !IsBrowserPage(name) && Uri.CheckHostName(name) == UriHostNameType.Dns;
    }

    /// <summary>
    /// A browser's own page rather than a website: its "host" has no dot (newtab, extensions, settings, favorites,
    /// chrome-extension, file…). localhost is a real site.
    /// </summary>
    private static bool IsBrowserPage(string site)
    {
        return !site.Contains('.') && !site.Equals("localhost", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>"today", "Sunday 18 October" or "the last 7 days".</summary>
    private string PeriodForQuestion => Period switch
    {
        HistoryPeriod.Week => "the last 7 days",
        HistoryPeriod.Day when SelectedDay?.Date != Today => $"{SelectedDay:dddd d MMMM}",
        _ => "today",
    };

    private Task DeleteAsync(string label, IEnumerable<UsageDeletion> parts)
    {
        return _deleted.DeleteAsync(new DeletedUsage(Guid.NewGuid(), label, PeriodLabel, _from, _to, parts.ToList()));
    }

    private Task HideAsync(UsageRowViewModel row)
    {
        return _hidden.SetHiddenAsync(row.RealNames, hidden: true);
    }

    private Avalonia.Media.Imaging.Bitmap? FindIcon(IEnumerable<string> realNames)
    {
        return realNames.Select(_icons.Find).FirstOrDefault(found => found is not null);
    }

    /// <summary>The top 5 rows, or all of them.</summary>
    private static void Show(ObservableCollection<UsageRowViewModel> target, List<UsageRowViewModel> all, bool showAll)
    {
        target.Clear();
        foreach (UsageRowViewModel row in showAll ? all : all.Take(TopCount))
        {
            target.Add(row);
        }
    }

    private Task RenameAsync(UsageRowViewModel row, string? name)
    {
        return _names.RenameAsync(row.RealNames, name);
    }

    private void RefreshIcons()
    {
        foreach (UsageRowViewModel row in _allDistractions.Concat(_allOverall).Concat(_allOverall.SelectMany(app => app.Sites)))
        {
            row.Icon = row.RealNames.Select(_icons.Find).FirstOrDefault(found => found is not null);
        }
    }

    private static string Percent(TimeSpan part, TimeSpan whole)
    {
        return whole <= TimeSpan.Zero ? "–" : $"{part.TotalSeconds / whole.TotalSeconds:P0}";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.FromMinutes(1))
        {
            return duration <= TimeSpan.Zero ? "0 min" : "<1 min";
        }

        int hours = (int)duration.TotalHours;
        return hours > 0 ? $"{hours} h {duration.Minutes} min" : $"{duration.Minutes} min";
    }
}
