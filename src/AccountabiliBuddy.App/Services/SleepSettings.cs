using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Sleep mode: bedtime and wake time, shown on the day bar and as a live "sleep if you go now" line on screen
/// in the evening. App-owned (documents "sleep"), display only.
/// </summary>
/// <param name="SameEveryNight">True = the weeknight times every night; false = Friday and Saturday nights use the weekend times.</param>
/// <param name="GoalHours">The sleep to aim for: the "sleep if you go now" countdown starts when bed now would give this.</param>
/// <param name="WakeOnce">A wake time changed for one night from the on-screen window, or null (the plan's).</param>
/// <param name="UsePhoneAlarm">The phone's next alarm sets the wake time (ADR 23); on by default.</param>
/// <param name="UsePhoneBedtime">The phone's smart bedtime sets the bedtime (ADR 34); on by default.</param>
/// <param name="SmartWorkdays">The smart bedtime is on for nights before workdays (shared both ways, ADR 35).</param>
/// <param name="SmartOffDays">The smart bedtime is on for nights before Off-Days.</param>
/// <param name="MinHours">The smart bedtime's minimum sleep.</param>
/// <param name="MaxHours">The smart bedtime's maximum sleep.</param>
/// <param name="BedtimeReminders">The bedtime reminders (ADR 38; shared); null = the defaults.</param>
/// <param name="UsePcAlarm">This PC rings its own alarm at the night's wake time, which is then the wake time (ADR 35); this PC's own, never shared.</param>
public sealed record SleepSettings(
    bool Enabled, bool SameEveryNight, SleepTimes Weeknights, SleepTimes Weekends, double GoalHours = 8, WakeOnce? WakeOnce = null,
    bool UsePhoneAlarm = true, bool UsePhoneBedtime = true,
    bool SmartWorkdays = false, bool SmartOffDays = false, double MinHours = 6.5, double MaxHours = 9, bool UsePcAlarm = false,
    BedtimeReminderSettings? BedtimeReminders = null)
{
    /// <summary>The bedtime reminders, the defaults when none were set (ADR 38).</summary>
    public BedtimeReminderSettings Reminders => BedtimeReminders ?? BedtimeReminderSettings.Default;

    /// <summary>The goals Settings offers, in hours.</summary>
    public static IReadOnlyList<double> GoalChoices { get; } = new[] { 6, 6.5, 7, 7.5, 8, 8.5, 9, 9.5, 10 };

    /// <summary>The goal as a length; 8 hours when the saved value makes no sense.</summary>
    public TimeSpan Goal => GoalHours is >= 4 and <= 12 ? TimeSpan.FromHours(GoalHours) : SleepSchedule.DefaultGoal;

    public static SleepSettings Default { get; } = new(
        Enabled: true,
        SameEveryNight: false,
        Weeknights: new SleepTimes(new TimeOnly(22, 0), new TimeOnly(6, 0)),
        Weekends: new SleepTimes(new TimeOnly(23, 30), new TimeOnly(7, 30)));

    /// <param name="plan">The day plan, which sets each night (its workdays and one-off days included); these
    /// settings' times are the fallback for a day whose plan has no night.</param>
    /// <param name="phoneAlarm">The phone's next alarm (local time), used while <see cref="UsePhoneAlarm"/> is on.</param>
    /// <param name="phoneBedtime">The phone's smart bedtime (ADR 34), used while <see cref="UsePhoneBedtime"/> is on.</param>
    /// <param name="phoneWake">The phone's estimated wake per kind of morning (ADR 40), for a night woken by the alarm with
    /// none known; used unless the wake time comes from neither alarm (<see cref="UsePhoneAlarm"/> or
    /// <see cref="UsePcAlarm"/> on), so this PC's alarm (ADR 35) rings at it too.</param>
    public SleepSchedule ToSchedule(
        DayPlan? plan = null, DateTime? phoneAlarm = null, AccountabiliBuddy.Core.Sync.SyncSmartBedtime? phoneBedtime = null,
        AccountabiliBuddy.Core.Sync.SyncWakeEstimate? phoneWake = null)
    {
        Func<DateOnly, TimeOnly?>? wakeGuess = null;
        if ((UsePhoneAlarm || UsePcAlarm) && plan is not null && phoneWake is not null)
        {
            wakeGuess = morning => ParseTime(plan.IsWorkday(morning, withOneOffDays: false) ? phoneWake.Workdays : phoneWake.OffDays);
        }

        Func<DateOnly, TimeSpan?>? bedLevel = null;
        if (UsePhoneBedtime && plan is not null && phoneBedtime is not null)
        {
            bedLevel = day => (plan.IsWorkday(day, withOneOffDays: false) ? phoneBedtime.Workdays : phoneBedtime.OffDays)?.LevelMinutes is int minutes
                ? TimeSpan.FromMinutes(minutes)
                : null;
        }

        return new SleepSchedule(
            Weeknights,
            SameEveryNight ? Weeknights : Weekends,
            plan is null ? null : day => DayPlanSleep.NightBefore(plan, day),
            WakeOnce,
            UsePhoneAlarm && !UsePcAlarm ? phoneAlarm : null,
            Goal,
            bedLevel,
            wakeGuess);
    }

    // "HH:mm" (invariant) from the phone's sync file; null when missing or unreadable.
    private static TimeOnly? ParseTime(string? text)
    {
        return TimeOnly.TryParseExact(text, "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out TimeOnly time)
            ? time
            : null;
    }
}
