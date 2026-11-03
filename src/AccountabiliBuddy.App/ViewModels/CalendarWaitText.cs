using System.Globalization;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>The words for a calendar change that waits for the looser-change delay (ADR 58). Times are 12-hour.</summary>
public static class CalendarWaitText
{
    /// <summary>"Takes effect at 4:05 PM", with the weekday ("Takes effect Tue at 12:03 AM") when it isn't today.</summary>
    public static string TakesEffect(DateTimeOffset at, TimeProvider time)
    {
        (string day, string clock) = Split(at, time);
        return day.Length == 0 ? $"Takes effect at {clock}" : $"Takes effect {day} at {clock}";
    }

    /// <summary>"Waiting until 4:05 PM", with the weekday ("Waiting until Tue 12:03 AM") when it isn't today.</summary>
    public static string WaitingUntil(DateTimeOffset at, TimeProvider time)
    {
        (string day, string clock) = Split(at, time);
        return day.Length == 0 ? $"Waiting until {clock}" : $"Waiting until {day} {clock}";
    }

    // The weekday (empty for today) and the 12-hour clock of a time.
    private static (string Day, string Clock) Split(DateTimeOffset at, TimeProvider time)
    {
        DateTime local = TimeZoneInfo.ConvertTime(at, time.LocalTimeZone).DateTime;
        DateTime today = TimeZoneInfo.ConvertTime(time.GetUtcNow(), time.LocalTimeZone).DateTime.Date;
        string day = local.Date == today ? string.Empty : local.ToString("ddd", CultureInfo.InvariantCulture);
        return (day, PlanTimelineBuilder.Clock(local));
    }
}
