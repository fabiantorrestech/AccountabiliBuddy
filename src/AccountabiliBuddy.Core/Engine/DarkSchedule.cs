using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// When the app should look dark: from a time of day to another (the set times) or from sunset to the next sunrise (ADR 49).
/// Pure: a moment in, the state then and the next flip out.
/// </summary>
public static class DarkSchedule
{
    // Days looked at on each side of today: enough to find the last flip before now and the first one after it, even when
    // a place's sun times land a day off its time zone's date.
    private const int DaysBefore = 2;
    private const int DaysAfter = 2;

    private readonly record struct Flip(DateTimeOffset At, bool ToDark);

    /// <summary>Dark from <paramref name="darkAt"/> until <paramref name="lightAt"/> (either may be the earlier time of day).</summary>
    public static DarkPlan ForSetTimes(DateTimeOffset now, TimeOnly darkAt, TimeOnly lightAt, TimeZoneInfo zone, bool usedSetTimes = false)
    {
        DateOnly today = DateOnly.FromDateTime(now.DateTime);
        if (darkAt == lightAt)
        {
            // Nothing to switch between: stays light, looked at again tomorrow.
            return new DarkPlan(false, LocalMoment(today.AddDays(1), TimeOnly.MinValue, zone), usedSetTimes);
        }

        var flips = new List<Flip>();
        for (int offset = -DaysBefore; offset <= DaysAfter; offset++)
        {
            DateOnly day = today.AddDays(offset);
            flips.Add(new Flip(LocalMoment(day, darkAt, zone), ToDark: true));
            flips.Add(new Flip(LocalMoment(day, lightAt, zone), ToDark: false));
        }

        return PlanFrom(flips, now, usedSetTimes);
    }

    /// <summary>
    /// Dark from sunset to the next sunrise at <paramref name="point"/>. Where the sun does not rise or set (polar day or
    /// night), the set times are used instead.
    /// </summary>
    public static DarkPlan ForSunset(DateTimeOffset now, GeoPoint point, TimeOnly fallbackDarkAt, TimeOnly fallbackLightAt, TimeZoneInfo zone)
    {
        DateOnly today = DateOnly.FromDateTime(now.DateTime);
        var flips = new List<Flip>();
        for (int offset = -DaysBefore; offset <= DaysAfter; offset++)
        {
            SunTimesResult? sun = SunTimes.Calculate(today.AddDays(offset), point, zone);
            if (sun is null)
            {
                return ForSetTimes(now, fallbackDarkAt, fallbackLightAt, zone, usedSetTimes: true);
            }

            flips.Add(new Flip(sun.Sunrise, ToDark: false));
            flips.Add(new Flip(sun.Sunset, ToDark: true));
        }

        return PlanFrom(flips, now, usedSetTimes: false);
    }

    private static DarkPlan PlanFrom(List<Flip> flips, DateTimeOffset now, bool usedSetTimes)
    {
        flips.Sort((a, b) => a.At.CompareTo(b.At));
        bool isDark = false;
        DateTimeOffset? next = null;
        foreach (Flip flip in flips)
        {
            if (flip.At <= now)
            {
                isDark = flip.ToDark;
            }
            else
            {
                next = flip.At;
                break;
            }
        }

        return new DarkPlan(isDark, next ?? now.AddHours(1), usedSetTimes);
    }

    private static DateTimeOffset LocalMoment(DateOnly day, TimeOnly time, TimeZoneInfo zone)
    {
        DateTime local = day.ToDateTime(time, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1); // Skipped by the clock going forward: the same moment, an hour later.
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}
