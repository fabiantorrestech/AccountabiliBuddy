namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// Which goal history file (ADR 79 shared history, GS1b): a device's month or a year's roll-up. Equal by value, so it keys what a device
/// has read. How it is named on Drive or the relay is the transport's (GS3, GS4).
/// </summary>
/// <param name="DeviceId">The month file's device; null for a year file.</param>
/// <param name="Month">1-12 for a month file; null for a year file.</param>
public sealed record GoalHistoryFileKey(GoalHistoryFileKind Kind, Guid? DeviceId, int Year, int? Month)
{
    public static GoalHistoryFileKey ForMonth(Guid deviceId, int year, int month) => new(GoalHistoryFileKind.Month, deviceId, year, month);

    public static GoalHistoryFileKey ForYear(int year) => new(GoalHistoryFileKind.Year, null, year, null);
}
