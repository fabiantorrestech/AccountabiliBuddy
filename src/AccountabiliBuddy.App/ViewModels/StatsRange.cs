namespace AccountabiliBuddy.App.ViewModels;

/// <summary>The kind of span the Stats page's views cover. The span itself, with its dates, is a <see cref="StatsPeriod"/>.</summary>
public enum StatsRange
{
    /// <summary>The selected day.</summary>
    Day,

    /// <summary>A week, Monday to Sunday.</summary>
    Week,

    /// <summary>A calendar month.</summary>
    Month,

    /// <summary>The last year, ending today (History keeps 90 days, so older cells stay empty).</summary>
    Year,

    /// <summary>The last 7 days, ending today.</summary>
    Last7,

    /// <summary>The last 30 days, ending today.</summary>
    Last30,

    /// <summary>The last 90 days, ending today.</summary>
    Last90,

    /// <summary>Two dates the user picked.</summary>
    Custom,
}
