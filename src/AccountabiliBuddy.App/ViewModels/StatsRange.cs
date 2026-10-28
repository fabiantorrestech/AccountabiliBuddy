namespace AccountabiliBuddy.App.ViewModels;

/// <summary>The span the Stats page's chart covers.</summary>
public enum StatsRange
{
    /// <summary>The selected day.</summary>
    Day,

    /// <summary>This week, Monday to Sunday.</summary>
    Week,

    /// <summary>This month.</summary>
    Month,

    /// <summary>The last year (History keeps 90 days, so older cells stay empty).</summary>
    Year,
}
