using System.Globalization;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The date after a page's title (Tasks redesign 4a.05-4a.06): "Tuesday · Nov 24", full weekday · short month and day, on Tasks, Today and
/// Goals alike (<see cref="Controls.PageHeader"/> shortens it to "Tuesday · …" when the header runs out of room).
/// </summary>
public static class PageDateText
{
    public static string Of(DateOnly day)
    {
        return day.ToString("dddd", CultureInfo.InvariantCulture) + " · " + day.ToString("MMM d", CultureInfo.InvariantCulture);
    }

    public static string Of(DateTime day) => Of(DateOnly.FromDateTime(day));
}
