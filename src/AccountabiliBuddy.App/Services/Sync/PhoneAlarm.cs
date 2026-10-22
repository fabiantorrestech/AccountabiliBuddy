using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>A paired phone's next alarm as this PC's local time (ADR 23): from the newest phone file that has one.</summary>
public static class PhoneAlarm
{
    /// <returns>The alarm while it's still ahead; null when no phone shares one.</returns>
    public static DateTime? Of(IReadOnlyList<ReceivedDevice> devices, TimeProvider time)
    {
        DateTimeOffset? alarm = devices
            .Where(device => device.File.Device.Kind == SyncDeviceCard.KindPhone && device.File.Device.NextAlarm is not null)
            .OrderByDescending(device => device.File.Device.WrittenAt)
            .Select(device => device.File.Device.NextAlarm)
            .FirstOrDefault();
        if (alarm is not DateTimeOffset next || next <= time.GetUtcNow())
        {
            return null;
        }

        return TimeZoneInfo.ConvertTime(next, time.LocalTimeZone).DateTime;
    }
}
