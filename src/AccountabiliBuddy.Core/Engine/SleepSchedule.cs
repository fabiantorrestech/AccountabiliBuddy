using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Bedtime and wake time worked out for actual dates. A night is named by the date of its evening: the night of
/// Friday the 23rd runs from Friday's bedtime to Saturday's wake time. With the day plan, each night's times come from
/// the plan for the day the night leads into (its workdays and one-off days included); otherwise, and for a day whose
/// plan has no night, Friday and Saturday nights use the weekend times. A wake time changed for one night
/// (<see cref="WakeOnce"/>) moves only that morning. All times are local wall-clock times.
/// </summary>
public sealed class SleepSchedule
{
    public static readonly TimeSpan GoodSleep = TimeSpan.FromHours(7);
    public static readonly TimeSpan OkaySleep = TimeSpan.FromHours(5);

    /// <summary>The sleep goal unless the user picks another: the countdown starts when bed now would give this.</summary>
    public static readonly TimeSpan DefaultGoal = TimeSpan.FromHours(8);

    // A bedtime before noon belongs to the early hours after the evening.
    private static readonly TimeOnly Noon = new(12, 0);

    private readonly SleepTimes _weeknights;
    private readonly SleepTimes _weekends;
    private readonly Func<DateOnly, SleepTimes?>? _nightBefore;
    private readonly WakeOnce? _wakeOnce;

    /// <param name="nightBefore">The times of the night before a day, from the day plan; null when it has none.</param>
    /// <param name="wakeOnce">A wake time changed for one morning only, or null.</param>
    public SleepSchedule(SleepTimes weeknights, SleepTimes weekends, Func<DateOnly, SleepTimes?>? nightBefore = null, WakeOnce? wakeOnce = null)
    {
        _weeknights = weeknights;
        _weekends = weekends;
        _nightBefore = nightBefore;
        _wakeOnce = wakeOnce;
    }

    public static bool IsWeekendNight(DateOnly evening)
    {
        return evening.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday;
    }

    public static SleepAmount Rate(TimeSpan sleep)
    {
        return sleep >= GoodSleep ? SleepAmount.Good : sleep >= OkaySleep ? SleepAmount.Okay : SleepAmount.Short;
    }

    /// <summary>Graded against the goal: good down to an hour under it, okay down to three hours under, short below.</summary>
    public static SleepAmount Rate(TimeSpan sleep, TimeSpan goal)
    {
        return sleep >= goal - TimeSpan.FromHours(1) ? SleepAmount.Good
            : sleep >= goal - TimeSpan.FromHours(3) ? SleepAmount.Okay
            : SleepAmount.Short;
    }

    /// <summary>From bedtime to wake time of the night starting on <paramref name="evening"/>.</summary>
    public LocalWindow Night(DateOnly evening)
    {
        SleepTimes times = _nightBefore?.Invoke(evening.AddDays(1)) ?? (IsWeekendNight(evening) ? _weekends : _weeknights);
        DateTime date = evening.ToDateTime(TimeOnly.MinValue);
        DateTime bed = date + times.Bedtime.ToTimeSpan();
        if (times.Bedtime < Noon)
        {
            bed = bed.AddDays(1);
        }

        DateOnly morning = evening.AddDays(1);
        TimeOnly wakeTime = _wakeOnce is not null && _wakeOnce.Morning == morning ? _wakeOnce.Wake : times.Wake;
        DateTime wake = date.AddDays(1) + wakeTime.ToTimeSpan();
        return new LocalWindow(bed, wake > bed ? wake : bed);
    }

    /// <summary>
    /// The night ahead of <paramref name="now"/>, or the one it is in: last night until its wake time, then
    /// tonight.
    /// </summary>
    public LocalWindow NightAround(DateTime now)
    {
        DateOnly today = DateOnly.FromDateTime(now);
        LocalWindow lastNight = Night(today.AddDays(-1));
        return now < lastNight.End ? lastNight : Night(today);
    }

    /// <summary>The waking day <paramref name="now"/> belongs to: from the last wake time to the next bedtime.</summary>
    public LocalWindow DayAround(DateTime now)
    {
        LocalWindow night = NightAround(now);
        DateOnly evening = DateOnly.FromDateTime(night.Start.TimeOfDay < Noon.ToTimeSpan() ? night.Start.AddDays(-1) : night.Start);
        LocalWindow nightBefore = Night(evening.AddDays(-1));
        return new LocalWindow(nightBefore.End, night.Start);
    }

    /// <summary>
    /// True from when going to bed would give <paramref name="goal"/> of sleep until wake time: the "sleep if you go
    /// now" countdown.
    /// </summary>
    public static bool ShowsCountdown(DateTime now, LocalWindow night, TimeSpan goal)
    {
        return now >= night.End - goal && now < night.End;
    }

    /// <summary>How long the sleep would be when going to bed at <paramref name="now"/>, until wake time.</summary>
    public static TimeSpan SleepIfBedNow(DateTime now, LocalWindow night)
    {
        return now < night.End ? night.End - now : TimeSpan.Zero;
    }

    /// <summary>How long the sleep would be when going to bed at <paramref name="now"/> (or at bedtime, if earlier).</summary>
    public static TimeSpan SleepIfBedAt(DateTime now, LocalWindow night)
    {
        DateTime asleep = now > night.Start ? now : night.Start;
        return asleep < night.End ? night.End - asleep : TimeSpan.Zero;
    }
}
