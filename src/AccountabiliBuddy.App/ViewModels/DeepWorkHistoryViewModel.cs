using System.Globalization;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Sync;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// History's deep work against the daily goal (redesign phase 4): one bar per day for the last 7 or 30 days, empty days
/// included, the goal as a line, and a summary (total, days the goal was met, longest unbroken stretch).
/// </summary>
public sealed partial class DeepWorkHistoryViewModel : ObservableObject
{
    private readonly Func<DateTimeOffset, DateTimeOffset, Task<IReadOnlyList<SessionRecord>>> _loadRecords;
    private readonly Func<IEnumerable<SyncSession>> _others;
    private readonly Func<DateTimeOffset, bool> _isOmitted;
    private readonly GoalService _goal;
    private readonly TimeProvider _time;

    /// <param name="loadRecords">This PC's sessions between two times, newest first.</param>
    /// <param name="others">The other devices' sessions (ADR 22).</param>
    public DeepWorkHistoryViewModel(
        Func<DateTimeOffset, DateTimeOffset, Task<IReadOnlyList<SessionRecord>>> loadRecords,
        Func<IEnumerable<SyncSession>> others,
        Func<DateTimeOffset, bool> isOmitted,
        GoalService goal,
        TimeProvider time)
    {
        _loadRecords = loadRecords;
        _others = others;
        _isOmitted = isOmitted;
        _goal = goal;
        _time = time;
        SummaryText = string.Empty;
    }

    [ObservableProperty]
    public partial IReadOnlyList<DeepWorkBar> Bars { get; set; } = Array.Empty<DeepWorkBar>();

    /// <summary>The goal's height on the chart, 0 to 1.</summary>
    [ObservableProperty]
    public partial double GoalLine { get; set; }

    /// <summary>E.g. "5h 20m in 7 days · goal met on 3 of 7 days · longest stretch 1h 20m".</summary>
    [ObservableProperty]
    public partial string SummaryText { get; set; }

    /// <summary>The last 30 days instead of 7.</summary>
    [ObservableProperty]
    public partial bool ShowsMonth { get; set; }

    partial void OnShowsMonthChanged(bool value)
    {
        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        int count = ShowsMonth ? 30 : 7;
        DateTimeOffset now = _time.GetUtcNow();
        TimeZoneInfo zone = _time.LocalTimeZone;
        DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        DateOnly from = today.AddDays(1 - count);
        IReadOnlyList<SessionRecord> records;
        try
        {
            records = await _loadRecords(now.AddDays(-count - 1), now.AddDays(1));
        }
        catch (SqliteException)
        {
            return; // Keep what was shown.
        }

        // A session still running on this PC is the newest one without an end (the same rule as the day bar).
        IReadOnlyList<DeepWorkDay> days = DeepWork.Days(records, _others(), _isOmitted, sessionRunning: true, from, today, now, zone);
        TimeSpan goal = TimeSpan.FromMinutes(Math.Max(1, _goal.Current.DailyMinutes));
        double scale = Math.Max(goal.TotalMinutes * 1.25, days.Max(day => day.Focused.TotalMinutes));
        Bars = days.Select(day => new DeepWorkBar(
                Label(day.Date, count),
                day.Focused.TotalMinutes / scale,
                day.Focused >= goal,
                day.Date == today,
                $"{day.Date.ToString("ddd d MMM", CultureInfo.CurrentCulture)} · {GoalProgress.Duration(day.Focused)}"))
            .ToList();
        GoalLine = goal.TotalMinutes / scale;
        TimeSpan total = days.Aggregate(TimeSpan.Zero, (sum, day) => sum + day.Focused);
        TimeSpan longest = days.Max(day => day.Longest);
        SummaryText = $"{GoalProgress.Duration(total)} in {count} days · goal met on {days.Count(day => day.Focused >= goal)} of {count} days"
            + (longest > TimeSpan.Zero ? $" · longest stretch {GoalProgress.Duration(longest)}" : string.Empty);
    }

    private static string Label(DateOnly date, int count)
    {
        return count <= 7 ? date.ToString("ddd", CultureInfo.CurrentCulture) : date.Day.ToString(CultureInfo.CurrentCulture);
    }
}
