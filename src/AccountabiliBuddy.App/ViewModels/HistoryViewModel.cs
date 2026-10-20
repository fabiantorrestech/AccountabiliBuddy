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
/// History tab: a summary of the local activity log for today or the last 7 days — focus time, how much of it was
/// on track or distracted, the top distractions, the most-used apps and sites, and the sessions with how they ended.
/// A session can be marked as not real (a test): it stays listed, greyed, but its focus time doesn't count.
/// Refreshes itself a few seconds after new entries are written (entries are written when an activity ends).
/// </summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    private const int TopCount = 5;
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromSeconds(3);

    private readonly IActivityLog _log;
    private readonly ConfigurationService _configuration;
    private readonly PrivacySettingsService _privacy;
    private readonly NotRealSessionsService _notReal;
    private readonly TimeProvider _timeProvider;
    private readonly DispatcherTimer _refreshSoon;

    public HistoryViewModel(
        IActivityLog log,
        ActivityRecorder recorder,
        ConfigurationService configuration,
        PrivacySettingsService privacy,
        NotRealSessionsService notReal,
        TimeProvider timeProvider)
    {
        _notReal = notReal;
        _notReal.Changed += (_, _) => _ = RefreshAsync();
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

    /// <summary>False = today, true = the last 7 days.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowToday))]
    public partial bool ShowWeek { get; set; }

    public bool ShowToday
    {
        get => !ShowWeek;
        set => ShowWeek = !value;
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

    partial void OnShowWeekChanged(bool value)
    {
        _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        DateTimeOffset localNow = _timeProvider.GetLocalNow();
        var startOfToday = new DateTimeOffset(localNow.Date, localNow.Offset);
        DateTimeOffset from = ShowWeek ? startOfToday.AddDays(-6) : startOfToday;
        DateTimeOffset to = startOfToday.AddDays(1);
        PeriodLabel = ShowWeek ? $"{from:ddd d MMM} – today" : $"Today, {localNow:dddd d MMMM}";

        UsageSummary summary;
        IReadOnlyList<SessionRecord> records;
        try
        {
            records = SessionHistoryBuilder.Build(await _log.GetSessionEventsAsync(from, to));
            List<TimeRange> notReal = records
                .Where(record => _notReal.IsNotReal(record.StartedAt))
                .Select(record => new TimeRange(record.StartedAt, record.EndedAt ?? localNow))
                .ToList();
            summary = await _log.GetSummaryAsync(from, to, notReal);
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

        Fill(TopDistractions, summary.TopDistractions);
        Fill(TopOverall, summary.TopOverall);
        HasNoDistractions = TopDistractions.Count == 0;
        HasNoUsage = TopOverall.Count == 0;

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
            SessionMode.Scheduled => "Scheduled",
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

        bool notReal = _notReal.IsNotReal(record.StartedAt);
        if (notReal)
        {
            outcome = "Not real · " + outcome;
        }

        return new SessionRowViewModel(when, $"{profile} · {mode}", length, outcome, record.Reason, record.StartedAt, notReal);
    }

    [RelayCommand]
    private async Task ToggleNotRealAsync(SessionRowViewModel row)
    {
        await _notReal.SetNotRealAsync(row.StartedAt, !row.IsNotReal);
    }

    private static void Fill(ObservableCollection<UsageRowViewModel> target, IReadOnlyList<UsageEntry> entries)
    {
        target.Clear();
        double max = entries.Count > 0 ? Math.Max(1, entries[0].Time.TotalSeconds) : 1;
        foreach (UsageEntry entry in entries.Take(TopCount))
        {
            target.Add(new UsageRowViewModel(entry.Name, FormatDuration(entry.Time), entry.Time.TotalSeconds / max));
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
