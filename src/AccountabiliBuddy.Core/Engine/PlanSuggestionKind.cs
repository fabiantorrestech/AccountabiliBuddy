namespace AccountabiliBuddy.Core.Engine;

/// <summary>What a Plan helper suggestion is about.</summary>
public enum PlanSuggestionKind
{
    /// <summary>A coming day's calendar covers most of its work: the suggestion may carry an edit that moves the work.</summary>
    MoveWork,

    /// <summary>Today's goal no longer fits in the work time left.</summary>
    GoalDoesNotFit,

    /// <summary>A day of next week follows no day type.</summary>
    NoDayType,
}
