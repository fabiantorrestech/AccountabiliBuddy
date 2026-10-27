namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// Which template each weekday follows (schema v13, ADR 47): a repeating cycle of 1 to <see cref="MaxWeeks"/> weeks, each a
/// weekday-to-template map. Week 1 starts on <see cref="StartsOn"/> (a Monday) and the cycle repeats from there, before it
/// too: the week of a date is the number of whole weeks since <see cref="StartsOn"/>, floored, modulo the number of weeks. A
/// weekday missing from the week in force follows the Off day template.
/// </summary>
/// <param name="Weeks">One weekday-to-template map per week of the cycle, week 1 first; 1 to <see cref="MaxWeeks"/> of them.</param>
/// <param name="StartsOn">The Monday week 1 starts on; with one week it makes no difference.</param>
public sealed record PlanCycle(IReadOnlyList<IReadOnlyList<WeekdayTemplate>> Weeks, DateOnly StartsOn)
{
    /// <summary>The longest cycle the plan allows.</summary>
    public const int MaxWeeks = 8;

    /// <summary>The week of the cycle <paramref name="date"/> is in, 0-based (week 1 is 0).</summary>
    public int WeekIndexFor(DateOnly date)
    {
        if (Weeks.Count == 0)
        {
            return 0;
        }

        int days = date.DayNumber - StartsOn.DayNumber;
        // Floor division, so the weeks before StartsOn repeat the cycle backwards (the week before week 1 is the last).
        int weeksSince = days >= 0 ? days / 7 : -((-days + 6) / 7);
        return ((weeksSince % Weeks.Count) + Weeks.Count) % Weeks.Count;
    }

    /// <summary>The week of the cycle <paramref name="date"/> is in, 1-based.</summary>
    public int WeekNumberFor(DateOnly date)
    {
        return WeekIndexFor(date) + 1;
    }

    /// <summary>The template id <paramref name="date"/> follows, or null when its weekday is missing from the week in force.</summary>
    public Guid? TemplateIdFor(DateOnly date)
    {
        return Weeks.Count == 0 ? null : Weeks[WeekIndexFor(date)].FirstOrDefault(entry => entry.Day == date.DayOfWeek)?.TemplateId;
    }
}
