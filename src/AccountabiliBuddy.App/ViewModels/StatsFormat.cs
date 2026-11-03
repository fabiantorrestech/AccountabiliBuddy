using System.Globalization;
using AccountabiliBuddy.Core.Engine;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>Dates, ranges and short texts the Stats page's builders share.</summary>
internal static class StatsFormat
{
    /// <summary>"2h 15m".</summary>
    public static string Duration(TimeSpan time) => GoalProgress.Duration(time);

    /// <summary>The Monday of the week <paramref name="date"/> is in.</summary>
    public static DateOnly MondayOf(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>Monday = 0 to Sunday = 6.</summary>
    public static int RowOf(DateOnly date) => ((int)date.DayOfWeek + 6) % 7;

    /// <summary>"Oct 26 – Nov 1".</summary>
    public static string DatesText(DateOnly from, DateOnly to)
    {
        return from == to
            ? from.ToString("ddd, MMM d", CultureInfo.CurrentCulture)
            : from.Year == to.Year
                ? $"{from.ToString("MMM d", CultureInfo.CurrentCulture)} – {to.ToString("MMM d", CultureInfo.CurrentCulture)}"
                : $"{from.ToString("MMM d, yyyy", CultureInfo.CurrentCulture)} – {to.ToString("MMM d, yyyy", CultureInfo.CurrentCulture)}";
    }

    /// <summary>A weekday's first letter, for the labels under the week's bars.</summary>
    public static string WeekdayLetter(DateOnly date)
    {
        return CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(date.DayOfWeek)[..1];
    }

    public static string WeekdayName(DayOfWeek day) => CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(day);

    /// <summary>"9 AM", "12 PM" for an hour 0 to 24 (24 is the next midnight).</summary>
    public static string Hour(int hour)
    {
        return $"{(hour % 12 == 0 ? 12 : hour % 12)} {(hour % 24 < 12 ? "AM" : "PM")}";
    }

    /// <summary>"9–11 AM", or "11 AM–1 PM" when the two ends are on different sides of noon.</summary>
    public static string HourRange(int start, int end)
    {
        bool sameHalf = start % 24 < 12 == end % 24 < 12;
        string from = sameHalf ? (start % 12 == 0 ? 12 : start % 12).ToString(CultureInfo.CurrentCulture) : Hour(start);
        return $"{from}–{Hour(end)}";
    }

    /// <summary>"6a", "12p": a column label of the Intensity grid.</summary>
    public static string HourShort(int hour)
    {
        return $"{(hour % 12 == 0 ? 12 : hour % 12)}{(hour < 12 ? "a" : "p")}";
    }

    /// <summary>"7h 05m" for the numerals; minutes always two digits.</summary>
    public static string HoursMinutes(TimeSpan time)
    {
        int hours = (int)time.TotalHours;
        return hours == 0 ? $"{time.Minutes}m" : $"{hours}h {time.Minutes:00}m";
    }
}
