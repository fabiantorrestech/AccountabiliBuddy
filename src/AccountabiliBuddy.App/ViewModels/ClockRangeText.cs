using System.Globalization;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A span of the day in 12-hour time: "2:00-3:00 PM", or "11:30 AM-1:00 PM" when it crosses noon.</summary>
public static class ClockRangeText
{
    public static string Of(DateTime start, DateTime end)
    {
        string startMeridiem = start.ToString("tt", CultureInfo.InvariantCulture);
        string endMeridiem = end.ToString("tt", CultureInfo.InvariantCulture);
        string startClock = start.ToString("h:mm", CultureInfo.InvariantCulture);
        string endClock = end.ToString("h:mm", CultureInfo.InvariantCulture);
        return startMeridiem == endMeridiem && start.Date == end.Date
            ? $"{startClock}-{endClock} {endMeridiem}"
            : $"{startClock} {startMeridiem}-{endClock} {endMeridiem}";
    }
}
