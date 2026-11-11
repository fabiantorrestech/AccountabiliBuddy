using System.Globalization;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// The origin lines a task shows (ADR 76), made once when the task is made and kept as text. Times are 12-hour with AM/PM, in the
/// local time zone of the device that made the task.
/// </summary>
public static class TaskOrigins
{
    public const string FromPhone = "Added from phone";
    public const string FromStep = "From a step";
    public const string CopyFromPc = "Copy from PC";
    public const string ComeBackTo = "Come back to";

    /// <summary>"Parked Tue 11/3 11:52 AM".</summary>
    public static string Parked(DateTimeOffset at, TimeZoneInfo zone)
    {
        DateTime local = TimeZoneInfo.ConvertTime(at, zone).DateTime;
        return "Parked " + local.ToString("ddd M/d h:mm tt", CultureInfo.InvariantCulture);
    }

    /// <summary>"Auto-saved Wed 11/5", for the day the shutdown card wrapped up.</summary>
    public static string AutoSaved(DateOnly day)
    {
        return "Auto-saved " + day.ToString("ddd M/d", CultureInfo.InvariantCulture);
    }
}
