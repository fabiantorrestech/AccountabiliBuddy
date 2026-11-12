using System.Text.Json.Serialization;
using AccountabiliBuddy.Core.Goals;

namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>
/// Goals Options › Charts on this PC (document "goals.settings", device-local, never synced, a device setting in backups; brief §4.3):
/// the week start every goal's week, streak and heatmap use, the heatmap's day labels and the extra tiles on goal pages. Display
/// and period maths only, nothing a guard reads, so changes apply at once in both modes.
/// </summary>
/// <param name="WeekStart">Sunday by default (brief §4.3).</param>
/// <param name="HeatmapLabels">All by default (boards Goals-PC; owner 2026-11-14, Q373 reversed): every row is 15 px, so seven labels always fit.</param>
/// <param name="ExtraTiles">Streak and Total tiles on goal pages; off by default (a goal's own switch in Edit goal overrides it).</param>
/// <param name="Review">Settings › Goals › Weekly review (R5.6); null = the defaults (<see cref="GoalReviewSettings.Default"/>).</param>
/// <param name="Reminders">Settings › Goals › Default reminders (R5.6); null = the defaults (<see cref="GoalReminderDefaults.Default"/>).</param>
public sealed record GoalPageSettings(
    GoalWeekStart WeekStart,
    GoalHeatmapLabels HeatmapLabels,
    GoalExtraTiles? ExtraTiles,
    GoalReviewSettings? Review = null,
    GoalReminderDefaults? Reminders = null)
{
    public static GoalPageSettings Default { get; } = new(GoalWeekStart.Sunday, GoalHeatmapLabels.All, GoalExtraTiles.None);

    /// <summary>The weekly review's settings (the defaults when none are stored).</summary>
    [JsonIgnore]
    public GoalReviewSettings ReviewSettings => (Review ?? GoalReviewSettings.Default).Clamped();

    /// <summary>The day the weekly review comes: the chosen day, else the week's first day.</summary>
    [JsonIgnore]
    public DayOfWeek ReviewDay => ReviewSettings.Day ?? GoalPeriods.FirstDay(WeekStart);

    /// <summary>The rule switches a new goal reminder starts with.</summary>
    [JsonIgnore]
    public GoalReminderDefaults ReminderDefaults => Reminders ?? GoalReminderDefaults.Default;

    /// <summary>The same settings with known values only.</summary>
    public GoalPageSettings Clamped()
    {
        return this with
        {
            WeekStart = Enum.IsDefined(WeekStart) ? WeekStart : GoalWeekStart.Sunday,
            HeatmapLabels = Enum.IsDefined(HeatmapLabels) ? HeatmapLabels : GoalHeatmapLabels.All,
            ExtraTiles = ExtraTiles ?? GoalExtraTiles.None,
            Review = Review?.Clamped(),
        };
    }
}
