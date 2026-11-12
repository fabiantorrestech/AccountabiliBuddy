namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>
/// Settings › Goals › Weekly review on this PC (round 2 R5.6; change note §4 "Weekly review card"; part of "goals.settings", device-local):
/// the day and time the review comes, how long a snooze lasts, and whether the weekly tidy-up runs straight after it. With
/// <see cref="TidyUpAfter"/> on, the tidy-up moves to the review's day and its own card stops showing (<see cref="Tasks.TaskTidyUpService"/>).
/// Suggestions only: nothing here locks, waits or feeds a guard.
/// </summary>
/// <param name="Day">The day it comes; null = the week's first day (Options › Charts › Week starts on), as before Settings › Goals existed.</param>
/// <param name="Time">The time it comes from (6:00 PM by default).</param>
/// <param name="SnoozeDays">A snooze's length: 1 or 2 days (change note §4).</param>
/// <param name="TidyUpAfter">Run the tidy-up straight after the review (off by default: the tidy-up card keeps its own day).</param>
public sealed record GoalReviewSettings(DayOfWeek? Day, TimeOnly Time, int SnoozeDays, bool TidyUpAfter)
{
    public const int MinSnoozeDays = 1;
    public const int MaxSnoozeDays = 2;

    public static GoalReviewSettings Default { get; } = new(null, new TimeOnly(18, 0), 1, false);

    /// <summary>The same settings with known values only.</summary>
    public GoalReviewSettings Clamped()
    {
        return this with
        {
            Day = Day is DayOfWeek day && Enum.IsDefined(day) ? day : null,
            SnoozeDays = Math.Clamp(SnoozeDays, MinSnoozeDays, MaxSnoozeDays),
        };
    }
}
