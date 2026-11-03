namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// A span of whole local dates the Stats page shows, with the kind it was made as. Everything is plain dates (no clock times),
/// so a daylight-saving change cannot shift a boundary. Calendar kinds (Week, Month) step to the neighbouring calendar period;
/// the others (Last 7/30/90, Year, Custom) step a whole window of the same length.
/// </summary>
internal readonly record struct StatsPeriod(StatsRange Kind, DateOnly From, DateOnly To)
{
    /// <summary>How many dates the period covers.</summary>
    public int Days => To.DayNumber - From.DayNumber + 1;

    /// <summary>
    /// The period of a kind. For Week and Month <paramref name="anchor"/> is any date inside it; for the rolling kinds, Year and
    /// Day it is the last (or only) date. Custom has no rule of its own: use <see cref="Custom"/>.
    /// </summary>
    public static StatsPeriod For(StatsRange kind, DateOnly anchor)
    {
        switch (kind)
        {
            case StatsRange.Week:
                DateOnly monday = StatsFormat.MondayOf(anchor);
                return new StatsPeriod(kind, monday, monday.AddDays(6));
            case StatsRange.Month:
                var first = new DateOnly(anchor.Year, anchor.Month, 1);
                return new StatsPeriod(kind, first, first.AddMonths(1).AddDays(-1));
            case StatsRange.Year:
                return new StatsPeriod(kind, anchor.AddDays(-364), anchor);
            case StatsRange.Last7:
                return new StatsPeriod(kind, anchor.AddDays(-6), anchor);
            case StatsRange.Last30:
                return new StatsPeriod(kind, anchor.AddDays(-29), anchor);
            case StatsRange.Last90:
                return new StatsPeriod(kind, anchor.AddDays(-89), anchor);
            default:
                return new StatsPeriod(StatsRange.Day, anchor, anchor);
        }
    }

    /// <summary>Two picked dates, in either order.</summary>
    public static StatsPeriod Custom(DateOnly a, DateOnly b)
    {
        return a <= b ? new StatsPeriod(StatsRange.Custom, a, b) : new StatsPeriod(StatsRange.Custom, b, a);
    }

    /// <summary>The period just before this one: the previous week or month, or a window of the same length ending the day before.</summary>
    public StatsPeriod Previous()
    {
        return Kind switch
        {
            StatsRange.Week or StatsRange.Month => For(Kind, From.AddDays(-1)),
            StatsRange.Day => For(Kind, From.AddDays(-1)),
            _ => new StatsPeriod(Kind, From.AddDays(-Days), From.AddDays(-1)),
        };
    }

    /// <summary>The period just after this one; a window that would run past <paramref name="today"/> ends on today instead.</summary>
    public StatsPeriod Next(DateOnly today)
    {
        switch (Kind)
        {
            case StatsRange.Week:
            case StatsRange.Month:
            case StatsRange.Day:
                return For(Kind, To.AddDays(1));
            default:
                DateOnly end = To.AddDays(Days);
                if (end > today)
                {
                    end = today;
                }

                return new StatsPeriod(Kind, end.AddDays(1 - Days), end);
        }
    }

    /// <summary>True when a later period exists that has begun: the period ends before <paramref name="today"/>.</summary>
    public bool HasNext(DateOnly today) => To < today;

    /// <summary>True when an earlier period exists that touches the data: this one starts after the first date with history.</summary>
    public bool HasPrevious(DateOnly firstData) => From > firstData;
}
