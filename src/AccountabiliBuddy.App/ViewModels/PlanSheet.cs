namespace AccountabiliBuddy.App.ViewModels;

/// <summary>The sheet open over the Plan page, if any.</summary>
public enum PlanSheet
{
    None,

    /// <summary>The month view: events, one-off days and a day's own plan ("Change a day").</summary>
    ChangeADay,

    /// <summary>The week cycle, the day types with their parts, and the categories.</summary>
    DayTypes,
}
