namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One segment of the This week card's bar: a goal that is on track, behind or not due yet (change note §4a).</summary>
public sealed record GoalWeekSegment(bool IsOnTrack, bool IsBehind, bool IsNotDue);
