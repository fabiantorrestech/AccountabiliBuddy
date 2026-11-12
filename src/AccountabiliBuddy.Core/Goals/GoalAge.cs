using System.Globalization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// How long a goal has been going (change note §4a): from the day it was created to today, minus its paused days. Units:
/// days under 2 weeks, then weeks under 2 months, then months, then "1 year 2 months". The tooltip reads
/// "Since Mon 5/4/2026 · 191 days (27 weeks)", adding "· paused 46 days" when there were any.
/// </summary>
/// <param name="Days">Days going (paused days left out).</param>
/// <param name="PausedDays">Paused days between its creation and today.</param>
/// <param name="Short">"6 months", for "6 months going".</param>
/// <param name="Tooltip">"Since Mon 5/4/2026 · 191 days (27 weeks) · paused 46 days".</param>
public sealed record GoalAge(int Days, int PausedDays, string Short, string Tooltip)
{
    public static GoalAge For(Goal goal, DateOnly today, TimeZoneInfo zone)
    {
        DateOnly created = goal.CreatedOn(zone);
        int calendarDays = Math.Max(0, today.DayNumber - created.DayNumber);
        int paused = 0;
        for (DateOnly day = created; day < today; day = day.AddDays(1))
        {
            if (goal.IsPausedOn(day))
            {
                paused++;
            }
        }

        int days = calendarDays - paused;
        string since = $"Since {created.ToString("ddd", CultureInfo.InvariantCulture)} {created.Month}/{created.Day}/{created.Year}";
        string tooltip = $"{since} · {Plural(days, "day")}" + (days >= 7 ? $" ({Plural(days / 7, "week")})" : string.Empty)
            + (paused > 0 ? $" · paused {Plural(paused, "day")}" : string.Empty);
        return new GoalAge(days, paused, ShortText(created, days), tooltip);
    }

    // The units: whole months between the creation day and the creation day plus the days going.
    private static string ShortText(DateOnly created, int days)
    {
        if (days < 14)
        {
            return Plural(days, "day");
        }

        DateOnly reached = created.AddDays(days);
        int months = ((reached.Year - created.Year) * 12) + reached.Month - created.Month - (reached.Day < created.Day ? 1 : 0);
        if (months < 2)
        {
            return Plural(days / 7, "week");
        }

        if (months < 12)
        {
            return Plural(months, "month");
        }

        int years = months / 12;
        int rest = months % 12;
        return rest == 0 ? Plural(years, "year") : $"{Plural(years, "year")} {Plural(rest, "month")}";
    }

    private static string Plural(int count, string unit)
    {
        return count == 1 ? $"1 {unit}" : $"{count} {unit}s";
    }
}
