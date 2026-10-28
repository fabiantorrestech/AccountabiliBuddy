namespace AccountabiliBuddy.Core.Engine;

/// <summary>How steadily the daily deep work goal was met over a range of days (the Stats page's Consistency view).</summary>
/// <param name="CurrentStreak">Workdays in a row the goal was met, up to the latest one counted.</param>
/// <param name="BestStreak">The longest run of workdays met in the range.</param>
/// <param name="WorkdaysCounted">Workdays that count: not off days, and not today while its goal isn't met yet.</param>
/// <param name="WorkdaysMet">Of those, the ones that reached the goal.</param>
/// <param name="MostReliableDay">The weekday whose workdays met the goal most often (at least two of them); null for none.</param>
/// <param name="MostReliablePercent">How often that weekday met it, 0 to 100.</param>
/// <param name="HasOffDays">The range has off days, which never count against the user.</param>
public sealed record ConsistencySummary(
    int CurrentStreak,
    int BestStreak,
    int WorkdaysCounted,
    int WorkdaysMet,
    DayOfWeek? MostReliableDay,
    int MostReliablePercent,
    bool HasOffDays)
{
    /// <summary>Share of the counted workdays that met the goal, 0 to 100; 0 with none counted.</summary>
    public int MetPercent => WorkdaysCounted == 0 ? 0 : (int)Math.Round(100.0 * WorkdaysMet / WorkdaysCounted);
}
