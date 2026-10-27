namespace AccountabiliBuddy.App.ViewModels;

/// <summary>The pages of the Setup DP Assistant, in order.</summary>
public enum DpStep
{
    Workdays,
    WorkTime,
    DeepWork,
    Lunch,
    Sleep,
    Weeks,
    Review,

    /// <summary>After Save: what was saved and what is waiting.</summary>
    Done,
}
