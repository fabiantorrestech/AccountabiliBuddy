namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One line of the detail's Progress tracking card ("Ticks itself at 15 minutes in a day"); greyed when "Coming soon".</summary>
public sealed record GoalSourceLine(string Text, bool IsComingSoon = false);
