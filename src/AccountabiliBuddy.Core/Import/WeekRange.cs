using System.Globalization;

namespace AccountabiliBuddy.Core.Import;

/// <summary>
/// A part of the week in minutes since Sunday 00:00: <see cref="Start"/> up to (not including) <see cref="End"/>,
/// with 0 &lt;= Start &lt; End &lt;= <see cref="MinutesPerWeek"/>.
/// </summary>
public readonly record struct WeekRange(int Start, int End)
{
    public const int MinutesPerDay = 24 * 60;
    public const int MinutesPerWeek = 7 * MinutesPerDay;

    /// <summary>
    /// The ranges for <paramref name="startMinute"/>–<paramref name="endMinute"/> (minutes since midnight) on
    /// <paramref name="day"/>. An end not after the start runs into the next day; Saturday night wraps to Sunday.
    /// </summary>
    public static IEnumerable<WeekRange> FromDay(DayOfWeek day, int startMinute, int endMinute)
    {
        int start = (int)day * MinutesPerDay + startMinute;
        int end = (int)day * MinutesPerDay + endMinute;
        if (endMinute <= startMinute)
        {
            end += MinutesPerDay;
        }

        return FromWeekMinutes(start, end);
    }

    /// <summary>The ranges for a span that may run past Saturday midnight (<paramref name="end"/> up to two weeks).</summary>
    public static IEnumerable<WeekRange> FromWeekMinutes(int start, int end)
    {
        if (start < 0 || start >= MinutesPerWeek || end <= start || end - start > MinutesPerWeek)
        {
            yield break;
        }

        if (end > MinutesPerWeek)
        {
            yield return new WeekRange(start, MinutesPerWeek);
            yield return new WeekRange(0, end - MinutesPerWeek);
        }
        else
        {
            yield return new WeekRange(start, end);
        }
    }

    /// <summary>E.g. "Mon 07:00–12:00".</summary>
    public static string Describe(int start, int end)
    {
        var culture = CultureInfo.CurrentCulture;
        string day = culture.DateTimeFormat.GetAbbreviatedDayName((DayOfWeek)(start / MinutesPerDay % 7));
        return $"{day} {Clock(start)}–{Clock(end)}";
    }

    public override string ToString()
    {
        return Describe(Start, End);
    }

    private static string Clock(int weekMinute)
    {
        int minute = weekMinute % MinutesPerDay;
        return $"{minute / 60:00}:{minute % 60:00}";
    }
}
