namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// The days Search filters' "When" covers (Tasks redesign 7.28-7.31), both ends included: Today; This week (from the system's first day of
/// the week through the six days after it); This month; Custom (From to To; the same date in both = one day; a blank end is open; ends given
/// the wrong way round are swapped). Pure: today and the first day of the week come in.
/// </summary>
public static class TaskSearchDates
{
    /// <summary>The first and last day; null for Any time (and for Custom with neither date).</summary>
    public static (DateOnly From, DateOnly To)? Range(TaskSearchFilters filters, DateOnly today, DayOfWeek firstDayOfWeek)
    {
        switch (filters.When)
        {
            case TaskSearchWhen.Today:
                return (today, today);
            case TaskSearchWhen.ThisWeek:
            {
                int back = ((int)today.DayOfWeek - (int)firstDayOfWeek + 7) % 7;
                DateOnly start = today.AddDays(-back);
                return (start, start.AddDays(6));
            }

            case TaskSearchWhen.ThisMonth:
            {
                var start = new DateOnly(today.Year, today.Month, 1);
                return (start, start.AddMonths(1).AddDays(-1));
            }

            case TaskSearchWhen.Custom:
            {
                if (filters.From is null && filters.To is null)
                {
                    return null;
                }

                DateOnly from = filters.From ?? DateOnly.MinValue;
                DateOnly to = filters.To ?? DateOnly.MaxValue;
                return from <= to ? (from, to) : (to, from);
            }

            default:
                return null;
        }
    }
}
