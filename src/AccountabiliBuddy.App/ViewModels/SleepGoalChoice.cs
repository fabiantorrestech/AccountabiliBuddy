namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One sleep goal in Settings → Sleep, e.g. 8.5 hours shown as "8 h 30 min".</summary>
public sealed record SleepGoalChoice(double Hours, string Text);
