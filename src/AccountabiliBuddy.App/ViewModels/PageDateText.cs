using System.Globalization;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The date after a page's title: "Saturday, Nov 28th" (owner 2026-11-28; was "Tuesday · Nov 24", Tasks redesign 4a.05-4a.06), full
/// weekday, short month and the day with its ordinal, on Tasks, Today and Goals alike (<see cref="Controls.PageHeader"/> shortens it to
/// "Saturday, …" when the header runs out of room).
/// </summary>
public static class PageDateText
{
    /// <summary>Between the weekday and the rest; the header shortens the date to what comes before it.</summary>
    public const string Separator = ", ";

    public static string Of(DateOnly day)
    {
        return day.ToString("dddd", CultureInfo.InvariantCulture) + Separator + day.ToString("MMM", CultureInfo.InvariantCulture) + " "
               + day.Day.ToString(CultureInfo.InvariantCulture) + OrdinalOf(day.Day);
    }

    /// <summary>"st", "nd", "rd" or "th" after a day of the month (11th, 12th and 13th take "th").</summary>
    public static string OrdinalOf(int day)
    {
        if (day % 100 is 11 or 12 or 13)
        {
            return "th";
        }

        return (day % 10) switch
        {
            1 => "st",
            2 => "nd",
            3 => "rd",
            _ => "th",
        };
    }

    public static string Of(DateTime day) => Of(DateOnly.FromDateTime(day));
}
