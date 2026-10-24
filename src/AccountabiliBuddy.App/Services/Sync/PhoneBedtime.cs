using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>A paired phone's smart bedtime (ADR 34): from the newest phone file that has one.</summary>
public static class PhoneBedtime
{
    /// <returns>Its smart bedtime; null when no phone shares one.</returns>
    public static SyncSmartBedtime? Of(IReadOnlyList<ReceivedDevice> devices)
    {
        return devices
            .Where(device => device.File.Device.Kind == SyncDeviceCard.KindPhone && device.File.SmartBedtime is not null)
            .OrderByDescending(device => device.File.Device.WrittenAt)
            .Select(device => device.File.SmartBedtime)
            .FirstOrDefault();
    }

    /// <summary>"7 h 15 m in bed before waking · last 2 weeks asleep 11:48 PM avg, 6 h 52 m · this week 3 of 4", or why not.</summary>
    public static string Describe(SyncSmartLevel? level)
    {
        if (level is null)
        {
            return "off on the phone";
        }

        var parts = new List<string>();
        parts.Add(level.LevelMinutes is int minutes ? $"{Hours(TimeSpan.FromMinutes(minutes))} in bed before waking" : $"learning ({level.Nights} of 5 nights)");
        if (level.AverageAsleep is string asleep && TimeOnly.TryParse(asleep, System.Globalization.CultureInfo.InvariantCulture, out TimeOnly time))
        {
            parts.Add($"last 2 weeks asleep {time.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture)} avg"
                + (level.AverageSleptMinutes is int slept ? $", {Hours(TimeSpan.FromMinutes(slept))}" : string.Empty));
        }

        if (level.AskedThisWeek > 0)
        {
            parts.Add($"this week {level.MadeThisWeek} of {level.AskedThisWeek}");
        }

        return string.Join(" · ", parts);
    }

    private static string Hours(TimeSpan span)
    {
        return span.Minutes == 0 ? $"{(int)span.TotalHours} h" : $"{(int)span.TotalHours} h {span.Minutes} m";
    }
}
