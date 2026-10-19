namespace AccountabiliBuddy.App.Services;

/// <summary>Which part of the day the day bar covers.</summary>
public enum DayBarRange
{
    /// <summary>From the day start to the day end, grown to whole hours when a block falls outside.</summary>
    FitToDay,

    /// <summary>Midnight to midnight.</summary>
    FullDay,
}
