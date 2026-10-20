using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Sleep mode: bedtime and wake time, shown on the day bar and as a live "sleep if you go now" line on screen
/// in the evening. App-owned (documents "sleep"), display only.
/// </summary>
/// <param name="SameEveryNight">True = the weeknight times every night; false = Friday and Saturday nights use the weekend times.</param>
public sealed record SleepSettings(bool Enabled, bool SameEveryNight, SleepTimes Weeknights, SleepTimes Weekends)
{
    public static SleepSettings Default { get; } = new(
        Enabled: true,
        SameEveryNight: false,
        Weeknights: new SleepTimes(new TimeOnly(22, 0), new TimeOnly(6, 0)),
        Weekends: new SleepTimes(new TimeOnly(23, 30), new TimeOnly(7, 30)));

    public SleepSchedule ToSchedule()
    {
        return new SleepSchedule(Weeknights, SameEveryNight ? Weeknights : Weekends);
    }
}
