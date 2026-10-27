using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// Which template each weekday follows (schema v12, ADR 47). Week A is always there; with week B set, weeks alternate
/// from <see cref="BStartsOn"/> (a Monday): that week is B, the next A, and so on, before it too. A weekday missing from
/// the week in force follows the Off day template.
/// </summary>
/// <param name="A">Weekday to template, week A.</param>
/// <param name="B">Weekday to template, week B; null = no alternating weeks.</param>
/// <param name="BStartsOn">The Monday a week B starts on; only read when <paramref name="B"/> is set.</param>
public sealed record PlanWeek(
    IReadOnlyList<WeekdayTemplate> A,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<WeekdayTemplate>? B = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? BStartsOn = null)
{
    /// <summary>The template id <paramref name="date"/> follows, or null when its weekday is missing from the week in force.</summary>
    public Guid? TemplateIdFor(DateOnly date)
    {
        IReadOnlyList<WeekdayTemplate> week = IsWeekB(date) ? B! : A;
        return week.FirstOrDefault(entry => entry.Day == date.DayOfWeek)?.TemplateId;
    }

    /// <summary>True when <paramref name="date"/> is in a week B: alternating weeks are on and its week is an even number of weeks from B's start.</summary>
    public bool IsWeekB(DateOnly date)
    {
        if (B is null || BStartsOn is not DateOnly start)
        {
            return false;
        }

        // Floor, so the weeks before B's start alternate too (-1 week = A, -2 = B).
        int weeks = (int)Math.Floor((date.DayNumber - start.DayNumber) / 7.0);
        return weeks % 2 == 0;
    }
}
