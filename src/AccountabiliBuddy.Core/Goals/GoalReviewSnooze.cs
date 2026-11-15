using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// When a weekly goals review snooze ends (owner, Q548), the same rule as the phone: always 9:00 AM local, never "now plus a span".
/// AccountabiliBuddy mode: 9:00 AM one day later, or two days later when Settings › Goals asks for 2 days (the second option).
/// ProductaBuddy mode: 9:00 AM on next week's review day, so the snooze lasts the week ("For this week").
/// </summary>
public static class GoalReviewSnooze
{
    /// <summary>The morning a snooze ends.</summary>
    public static readonly TimeOnly EndsAt = new(9, 0);

    /// <param name="productaBuddy">The ProductaBuddy companion mode (ADR 68, 69).</param>
    /// <param name="due">The moment this week's review came due.</param>
    /// <param name="now">When the snooze is made.</param>
    /// <param name="days">AccountabiliBuddy mode's choice, 1 or 2 (ignored in ProductaBuddy mode).</param>
    public static DateTimeOffset Until(bool productaBuddy, DateTimeOffset due, DateTimeOffset now, int days, TimeZoneInfo zone)
    {
        if (productaBuddy)
        {
            DateOnly reviewDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(due, zone).DateTime);
            return TaskReminderTimes.Local(reviewDay.AddDays(7), EndsAt, zone);
        }

        DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        return TaskReminderTimes.Local(today.AddDays(days), EndsAt, zone);
    }
}
