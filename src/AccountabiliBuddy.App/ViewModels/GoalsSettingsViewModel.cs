using System.Globalization;
using AccountabiliBuddy.App.Services.Goals;
using AccountabiliBuddy.Core.Goals;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Settings › Goals (round 2 R5.6, brief §4.7): the weekly review (day, time, snooze length, run the tidy-up straight after: then the
/// tidy-up moves to the review's day and its own card stops showing), default reminders (Skip when done; Nudge when behind per mode,
/// §5.10), charts (week start, heatmap day labels), the extra tiles default, Areas, the link to Integrations (Beeminder, Google Calendar),
/// and the pausing rules in words. Everything is this PC's "goals.settings" (device-local), saved at once: display and suggestions only,
/// nothing a guard reads, so it isn't locked during a session.
/// </summary>
public sealed partial class GoalsSettingsViewModel : ObservableObject
{
    private static readonly GoalWeekStart[] WeekStarts = [GoalWeekStart.Sunday, GoalWeekStart.Monday, GoalWeekStart.Saturday];
    private static readonly GoalMonthLines[] MonthLineValues = [GoalMonthLines.Darker, GoalMonthLines.Normal, GoalMonthLines.Lighter, GoalMonthLines.Off];
    private static readonly GoalHeatmapLabels[] LabelValues = [GoalHeatmapLabels.All, GoalHeatmapLabels.Some, GoalHeatmapLabels.None];

    private readonly GoalsService _goals;
    private bool _loading;

    public GoalsSettingsViewModel(GoalsService goals)
    {
        _goals = goals;
        _goals.Store.Changed += (_, _) => Load();
        Load();
    }

    /// <summary>Areas… (set by the app: the Areas dialog).</summary>
    public Action? OpenAreas { get; set; }

    /// <summary>Connections › Integrations (set by the app: Settings › Connections, where Beeminder and calendars are connected).</summary>
    public Action? OpenIntegrations { get; set; }

    public IReadOnlyList<string> ReviewDayChoices { get; } =
        ["Week's first day", "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    public IReadOnlyList<string> SnoozeChoices { get; } = ["1 day", "2 days"];

    public IReadOnlyList<string> WeekStartChoices { get; } = ["Sun", "Mon", "Sat"];

    public IReadOnlyList<string> LabelChoices { get; } = ["All", "Some", "None"];

    public IReadOnlyList<string> MonthLineChoices { get; } = ["Darker", "Normal", "Lighter", "Off"];

    public string PausingRules =>
        "Pausing a goal (Active off in Edit goal) applies at once: it stops counting toward This week, stops its reminders and nudges, and "
        + "keeps all its history. Paused days are neither hit nor missed, so streaks and pace aren't punished; Stats shows the stretch "
        + "hatched. Turning it back on counts from that day. Deep work can't be paused or deleted: Track time only keeps counting with no "
        + "target, and like lowering its target it waits and never starts during a session.";

    public string ConnectionsText => "Connect Beeminder and your calendars once in Integrations, then pick what counts per goal in Edit goal › Tracking & sources.";

    [ObservableProperty]
    public partial int ReviewDayIndex { get; set; }

    /// <summary>"6:00 PM".</summary>
    [ObservableProperty]
    public partial string ReviewTimeText { get; set; } = "6:00 PM";

    [ObservableProperty]
    public partial string? ReviewTimeProblem { get; set; }

    [ObservableProperty]
    public partial int SnoozeIndex { get; set; }

    [ObservableProperty]
    public partial bool TidyUpAfter { get; set; }

    [ObservableProperty]
    public partial bool SkipWhenDone { get; set; }

    [ObservableProperty]
    public partial bool NudgeAccountabiliBuddy { get; set; }

    [ObservableProperty]
    public partial bool NudgeProductaBuddy { get; set; }

    [ObservableProperty]
    public partial int WeekStartIndex { get; set; }

    [ObservableProperty]
    public partial int LabelsIndex { get; set; }

    [ObservableProperty]
    public partial int MonthLinesIndex { get; set; }

    [ObservableProperty]
    public partial bool StreakTile { get; set; }

    [ObservableProperty]
    public partial bool TotalTile { get; set; }

    [ObservableProperty]
    public partial string AreasText { get; set; } = string.Empty;

    /// <summary>"Sundays at 6:00 PM, then the tidy-up": the review row's line.</summary>
    [ObservableProperty]
    public partial string ReviewSummary { get; set; } = string.Empty;

    public bool HasReviewTimeProblem => ReviewTimeProblem is not null;

    public bool IsReady => _goals.Store.IsReady;

    /// <summary>Reads the stored settings again (also after a change made elsewhere, e.g. Goals Options).</summary>
    public void Load()
    {
        _loading = true;
        try
        {
            GoalPageSettings settings = _goals.Store.PageSettings;
            GoalReviewSettings review = settings.ReviewSettings;
            ReviewDayIndex = review.Day is DayOfWeek day ? (int)day + 1 : 0;
            if (GoalReminderEditRow.ParseTime(ReviewTimeText) != review.Time)
            {
                ReviewTimeText = review.Time.ToString("h:mm tt", CultureInfo.InvariantCulture);
            }

            SnoozeIndex = review.SnoozeDays >= 2 ? 1 : 0;
            TidyUpAfter = review.TidyUpAfter;
            GoalReminderDefaults reminders = settings.ReminderDefaults;
            SkipWhenDone = reminders.SkipWhenDone;
            NudgeAccountabiliBuddy = reminders.NudgeWhenBehindAccountabiliBuddy;
            NudgeProductaBuddy = reminders.NudgeWhenBehindProductaBuddy;
            WeekStartIndex = Math.Max(0, Array.IndexOf(WeekStarts, settings.WeekStart));
            LabelsIndex = Math.Max(0, Array.IndexOf(LabelValues, settings.HeatmapLabels));
            MonthLinesIndex = Math.Max(0, Array.IndexOf(MonthLineValues, settings.MonthLines));
            GoalExtraTiles tiles = settings.ExtraTiles ?? GoalExtraTiles.None;
            StreakTile = tiles.Streak;
            TotalTile = tiles.Total;
            int areas = _goals.Lists.Areas.Count(area => area.IsLive);
            AreasText = areas == 1 ? "1 area" : $"{areas} areas";
            string days = settings.ReviewDay.ToString() + "s";
            ReviewSummary = $"{days} at {review.Time.ToString("h:mm tt", CultureInfo.InvariantCulture)}{(review.TidyUpAfter ? ", then the tidy-up" : string.Empty)}";
        }
        finally
        {
            _loading = false;
        }

        OnPropertyChanged(nameof(IsReady));
    }

    /// <summary>Saves what the page shows (at once; nothing waits: display and suggestions only).</summary>
    public Task SaveAsync()
    {
        GoalPageSettings settings = _goals.Store.PageSettings;
        TimeOnly time = GoalReminderEditRow.ParseTime(ReviewTimeText) ?? settings.ReviewSettings.Time;
        var review = new GoalReviewSettings(
            ReviewDayIndex <= 0 ? null : (DayOfWeek)(ReviewDayIndex - 1),
            time,
            SnoozeIndex == 1 ? 2 : 1,
            TidyUpAfter);
        GoalPageSettings changed = settings with
        {
            WeekStart = WeekStarts[Math.Clamp(WeekStartIndex, 0, WeekStarts.Length - 1)],
            HeatmapLabels = LabelValues[Math.Clamp(LabelsIndex, 0, LabelValues.Length - 1)],
            MonthLines = MonthLineValues[Math.Clamp(MonthLinesIndex, 0, MonthLineValues.Length - 1)],
            ExtraTiles = new GoalExtraTiles(StreakTile, TotalTile),
            // Written only once the review itself was changed (milestone review NIT3): the "goals.page" What's new entry counts a stored
            // review as the user's choice, so a change to any other row must not store the defaults for them.
            Review = settings.Review is null && review == settings.ReviewSettings ? null : review,
            Reminders = new GoalReminderDefaults(SkipWhenDone, NudgeAccountabiliBuddy, NudgeProductaBuddy),
        };
        return _goals.SetPageSettingsAsync(changed, this);
    }

    [RelayCommand]
    private void Areas() => OpenAreas?.Invoke();

    [RelayCommand]
    private void Integrations() => OpenIntegrations?.Invoke();

    partial void OnReviewDayIndexChanged(int value) => Edited();

    partial void OnReviewTimeTextChanged(string value)
    {
        ReviewTimeProblem = GoalReminderEditRow.ParseTime(value) is null ? "Type a time like 6:00 PM." : null;
        if (ReviewTimeProblem is null)
        {
            Edited();
        }
    }

    partial void OnReviewTimeProblemChanged(string? value) => OnPropertyChanged(nameof(HasReviewTimeProblem));

    partial void OnSnoozeIndexChanged(int value) => Edited();

    partial void OnTidyUpAfterChanged(bool value) => Edited();

    partial void OnSkipWhenDoneChanged(bool value) => Edited();

    partial void OnNudgeAccountabiliBuddyChanged(bool value) => Edited();

    partial void OnNudgeProductaBuddyChanged(bool value) => Edited();

    partial void OnWeekStartIndexChanged(int value) => Edited();

    partial void OnLabelsIndexChanged(int value) => Edited();

    partial void OnMonthLinesIndexChanged(int value) => Edited();

    partial void OnStreakTileChanged(bool value) => Edited();

    partial void OnTotalTileChanged(bool value) => Edited();

    private void Edited()
    {
        if (!_loading)
        {
            _ = SaveAsync();
        }
    }
}
