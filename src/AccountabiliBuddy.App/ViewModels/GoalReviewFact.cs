namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A fact pill on the weekly review's goal card ("2 days behind" in amber, "Pace: 0.46 lb a week", "At this pace: Mar 3").</summary>
public sealed record GoalReviewFact(string Text, bool IsAmber = false);
