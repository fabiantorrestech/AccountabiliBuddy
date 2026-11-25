using AccountabiliBuddy.Core.Domain;
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

    /// <summary>The open tasks the one-time move (and an older device's new items) brought into To sort (MIG1).</summary>
    public const string EarlierVersion = "From the earlier version";

    /// <summary>"Done again from 142" (a History copy of a finished task); "Done again" when the original had no number.</summary>
    public static string DoneAgain(TaskNumber? original)
    {
        return original is null ? "Done again" : "Done again from " + original.Value;
    }

    /// <summary>"Parked Tue 11/3 11:52 AM".</summary>
    public static string Parked(DateTimeOffset at, TimeZoneInfo zone)
    {
        DateTime local = TimeZoneInfo.ConvertTime(at, zone).DateTime;
        return "Parked " + local.ToString("ddd M/d h:mm tt", CultureInfo.InvariantCulture);
    }

    /// <summary>"Session note Tue 11/24 4:05 PM" (ADR 94): a To sort task made from a line typed in Focus mode, at the note's time.</summary>
    public static string SessionNote(DateTimeOffset at, TimeZoneInfo zone)
    {
        DateTime local = TimeZoneInfo.ConvertTime(at, zone).DateTime;
        return "Session note " + local.ToString("ddd M/d h:mm tt", CultureInfo.InvariantCulture);
    }

    /// <summary>"Auto-saved Wed 11/5", for the day the shutdown card wrapped up.</summary>
    public static string AutoSaved(DateOnly day)
    {
        return "Auto-saved " + day.ToString("ddd M/d", CultureInfo.InvariantCulture);
    }
}
