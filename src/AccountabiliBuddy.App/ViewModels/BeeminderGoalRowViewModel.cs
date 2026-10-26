using AccountabiliBuddy.App.Services.Beeminder;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One Beeminder goal on Today (ADR 43): title, what's needed, safe days, the deadline in 12-hour time.</summary>
public sealed class BeeminderGoalRowViewModel
{
    public BeeminderGoalRowViewModel(BeeminderGoal goal, DateTimeOffset now, TimeZoneInfo zone)
    {
        DateTime due = TimeZoneInfo.ConvertTime(goal.DueAt, zone).DateTime;
        DateTime today = TimeZoneInfo.ConvertTime(now, zone).Date;
        Title = goal.Title;
        Needed = goal.Needed;
        // Red when there's no buffer left or the deadline falls today (the safe days are from the last answer).
        IsDueToday = goal.SafeDays <= 0 || due.Date <= today;
        SafeText = IsDueToday ? "Due today" : goal.SafeDays == 1 ? "1 safe day" : $"{goal.SafeDays} safe days";
        DueText = due.Date == today ? $"by {due:h:mm tt}"
            : due.Date == today.AddDays(1) ? $"by tomorrow {due:h:mm tt}"
            : due.Date < today ? $"was due {due:ddd h:mm tt}"
            : $"by {due:ddd h:mm tt}";
    }

    public string Title { get; }

    public string Needed { get; }

    public bool HasNeeded => Needed.Length > 0;

    public string SafeText { get; }

    public string DueText { get; }

    public bool IsDueToday { get; }
}
