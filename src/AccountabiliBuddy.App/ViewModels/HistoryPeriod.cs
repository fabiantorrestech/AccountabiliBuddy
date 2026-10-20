namespace AccountabiliBuddy.App.ViewModels;

/// <summary>What the History tab covers.</summary>
public enum HistoryPeriod
{
    Today,

    /// <summary>The last 7 days, today included.</summary>
    Week,

    /// <summary>One chosen day.</summary>
    Day,
}
