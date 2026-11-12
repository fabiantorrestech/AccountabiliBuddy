using System.Globalization;
using System.Text.RegularExpressions;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// The "Snooze until…" picker's rules (spec 05 §8), pure: a typed time ("930" is 9:30, A/P picks the half of the day), a month that
/// already passed this year rolls to next year, the month grid from Monday, natural typing ("thu 10:30", "tomorrow 9am") and the
/// status line. Times are 12-hour with AM/PM wherever they are shown.
/// </summary>
public static partial class TaskSnoozePicker
{
    public const string TypeHint = "Type a time like 930 for 9:30";

    public const string PassedText = "That time has already passed";

    /// <summary>
    /// The picker's time field: digits only count ("930", "9:30", "1030", "9"). Hours 1 to 12 follow <paramref name="pm"/> (the
    /// field's AM/PM toggle); 13 to 23 and 0 are read as the 24-hour clock. Null when it isn't a time (empty, more than 4 digits,
    /// minutes over 59, hours over 23).
    /// </summary>
    public static TimeOnly? ParseTime(string text, bool pm)
    {
        string digits = new(text.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length is 0 or > 4)
        {
            return null;
        }

        int hour = int.Parse(digits.Length <= 2 ? digits : digits[..^2], CultureInfo.InvariantCulture);
        int minute = digits.Length <= 2 ? 0 : int.Parse(digits[^2..], CultureInfo.InvariantCulture);
        if (hour > 23 || minute > 59)
        {
            return null;
        }

        if (hour >= 13 || hour == 0)
        {
            return new TimeOnly(hour, minute);
        }

        return new TimeOnly(hour == 12 ? (pm ? 12 : 0) : (pm ? hour + 12 : hour), minute);
    }

    /// <summary>
    /// A time written out, as in a snooze shortcut: "6:00 PM", "6pm", "6:30p", "930", "18:00". Without AM/PM, 1 to 11 are the morning,
    /// 12 is noon and 13 to 23 and 0 the 24-hour clock. Null when it isn't a time.
    /// </summary>
    public static TimeOnly? ParseClock(string text)
    {
        Match match = ClockPattern().Match(text.Replace(" ", string.Empty, StringComparison.Ordinal));
        if (!match.Success)
        {
            return null;
        }

        (int hour, int minute, string suffix) = Parts(match);
        if (suffix.Length > 0)
        {
            return Twelve(hour, minute, suffix.StartsWith('p'));
        }

        if (hour > 23 || minute > 59)
        {
            return null;
        }

        return new TimeOnly(hour, minute);
    }

    /// <summary>The time as the field shows it, without AM/PM: "9:30".</summary>
    public static string FieldText(TimeOnly time)
    {
        return time.ToString("h:mm", CultureInfo.InvariantCulture);
    }

    /// <summary><paramref name="day"/> of <paramref name="month"/> in <paramref name="year"/>, the day kept inside the month (31 → 30 in November).</summary>
    public static DateOnly Clamped(int year, int month, int day)
    {
        return new DateOnly(year, month, Math.Clamp(day, 1, DateTime.DaysInMonth(year, month)));
    }

    /// <summary>
    /// The first <paramref name="month"/>/<paramref name="day"/> on or after <paramref name="today"/>, from <paramref name="fromYear"/>
    /// (this year when null) on: a day already past rolls to the next year.
    /// </summary>
    public static DateOnly Upcoming(DateOnly today, int month, int day, int? fromYear = null)
    {
        for (int year = Math.Max(fromYear ?? today.Year, 1); ; year++)
        {
            DateOnly date = Clamped(year, month, day);
            if (date >= today)
            {
                return date;
            }
        }
    }

    /// <summary>The month grid: whole weeks from Monday, the days of the months around it included.</summary>
    public static IReadOnlyList<DateOnly> MonthDays(int year, int month)
    {
        var first = new DateOnly(year, month, 1);
        int lead = ((int)first.DayOfWeek + 6) % 7;
        int count = DateTime.DaysInMonth(year, month);
        int cells = (lead + count + 6) / 7 * 7;
        return Enumerable.Range(0, cells).Select(index => first.AddDays(index - lead)).ToList();
    }

    /// <summary>The status line for a picked moment: "Snoozes until Thu, Nov 12 · 9:00 AM" (the year when it isn't this year).</summary>
    public static string Until(DateTimeOffset moment, DateTimeOffset now, TimeZoneInfo zone)
    {
        DateTime local = TimeZoneInfo.ConvertTime(moment, zone).DateTime;
        int thisYear = TimeZoneInfo.ConvertTime(now, zone).Year;
        string year = local.Year == thisYear ? string.Empty : ", " + local.Year.ToString(CultureInfo.InvariantCulture);
        return "Snoozes until " + local.ToString("ddd, MMM d", CultureInfo.InvariantCulture) + year + " · " + TaskSnooze.Clock(TimeOnly.FromDateTime(local));
    }

    /// <summary>
    /// Natural typing: a day ("today", "tomorrow", "tmr", a weekday like "thu" or "thursday", "next mon") and a time ("10:30", "930",
    /// "5pm", "5:30 p"), in either order. Without a day: today, or tomorrow when that time has passed. A weekday is the next one (today
    /// when the time is still ahead; "next" skips today). A time without AM/PM is the next one ahead today; on another day 7 to 11 are
    /// the morning and 12 to 6 the afternoon or evening. Null when no time is found or a word isn't understood.
    /// </summary>
    public static (DateOnly Date, TimeOnly Time)? Natural(string text, DateTimeOffset now, TimeZoneInfo zone)
    {
        DateTime local = TimeZoneInfo.ConvertTime(now, zone).DateTime;
        DateOnly today = DateOnly.FromDateTime(local);
        TimeOnly clock = TimeOnly.FromDateTime(local);

        DateOnly? day = null;
        DayOfWeek? weekday = null;
        bool next = false;
        string timeText = string.Empty;
        foreach (string word in text.ToLowerInvariant().Split([' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (word == "next")
            {
                next = true;
            }
            else if (word is "at" or "on")
            {
                // "thu at 10:30", "on fri 9am": filler words.
            }
            else if (word is "today" or "tonight")
            {
                day = today;
            }
            else if (word is "tomorrow" or "tmr" or "tmrw" or "tom")
            {
                day = today.AddDays(1);
            }
            else if (WeekdayOf(word) is DayOfWeek found)
            {
                weekday = found;
            }
            else if (word is "am" or "pm" or "a" or "p" || word.Any(char.IsAsciiDigit))
            {
                timeText += word;
            }
            else
            {
                return null;
            }
        }

        Match match = ClockPattern().Match(timeText);
        if (timeText.Length == 0 || !match.Success)
        {
            return null;
        }

        (int hour, int minute, string suffix) = Parts(match);
        if (hour > 23 || minute > 59)
        {
            return null;
        }

        if (weekday is DayOfWeek wanted)
        {
            int ahead = ((int)wanted - (int)today.DayOfWeek + 7) % 7;
            day = today.AddDays(next && ahead == 0 ? 7 : ahead);
        }

        TimeOnly? fixedTime = suffix.Length > 0 ? Twelve(hour, minute, suffix.StartsWith('p'))
            : hour is >= 13 or 0 ? new TimeOnly(hour, minute)
            : null;
        if (suffix.Length > 0 && fixedTime is null)
        {
            return null;
        }

        // A time without AM/PM: the morning or the afternoon, depending on the day.
        TimeOnly Pick(DateOnly date)
        {
            if (fixedTime is TimeOnly known)
            {
                return known;
            }

            var morning = new TimeOnly(hour == 12 ? 0 : hour, minute);
            var afternoon = new TimeOnly(hour == 12 ? 12 : hour + 12, minute);
            if (date == today)
            {
                return hour != 12 && morning > clock ? morning : afternoon;
            }

            return hour is >= 7 and <= 11 ? morning : afternoon;
        }

        DateOnly picked = day ?? today;
        TimeOnly time = Pick(picked);
        bool canRoll = day is null || weekday is not null && !next;
        if (canRoll && picked == today && time <= clock)
        {
            picked = today.AddDays(weekday is null ? 1 : 7);
            time = Pick(picked);
        }

        return (picked, time);
    }

    private static (int Hour, int Minute, string Suffix) Parts(Match match)
    {
        int hour = int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture);
        int minute = match.Groups["m"].Success ? int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
        return (hour, minute, match.Groups["ap"].Value.ToLowerInvariant());
    }

    // 1 to 12 with AM or PM; null for anything else.
    private static TimeOnly? Twelve(int hour, int minute, bool pm)
    {
        if (hour is < 1 or > 12 || minute > 59)
        {
            return null;
        }

        return new TimeOnly(hour == 12 ? (pm ? 12 : 0) : (pm ? hour + 12 : hour), minute);
    }

    private static DayOfWeek? WeekdayOf(string word)
    {
        if (word.Length < 3)
        {
            return null;
        }

        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
        {
            if (day.ToString().ToLowerInvariant().StartsWith(word, StringComparison.Ordinal))
            {
                return day;
            }
        }

        return null;
    }

    [GeneratedRegex(@"^(?<h>\d{1,2})(?::?(?<m>\d{2}))?(?<ap>am|pm|a|p)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClockPattern();
}
