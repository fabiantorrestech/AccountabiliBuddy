using System.Globalization;

namespace AccountabiliBuddy.App.Services;

/// <summary>The overlay's time text: whole minutes by default ("18 min", "less than 1 min", "1 h 10 m"), minutes and seconds only in the "18:42" style.</summary>
public static class OverlayTimeText
{
    public static string Left(TimeSpan left, bool seconds)
    {
        if (left < TimeSpan.Zero)
        {
            left = TimeSpan.Zero;
        }

        if (seconds)
        {
            return left.TotalHours >= 1
                ? $"{(int)left.TotalHours}:{left.Minutes:00}:{left.Seconds:00}"
                : $"{(int)left.TotalMinutes}:{left.Seconds:00}";
        }

        int minutes = (int)left.TotalMinutes;
        if (minutes < 1)
        {
            return "less than 1 min";
        }

        return minutes >= 60 ? $"{minutes / 60} h {minutes % 60:00} m" : $"{minutes} min";
    }

    /// <summary>"1:38 PM" (12-hour, always).</summary>
    public static string Clock(DateTime time)
    {
        return time.ToString("h:mm tt", CultureInfo.InvariantCulture);
    }

    /// <summary>"Tue, Oct 27".</summary>
    public static string Date(DateTime time)
    {
        return time.ToString("ddd, MMM d", CultureInfo.InvariantCulture);
    }
}
