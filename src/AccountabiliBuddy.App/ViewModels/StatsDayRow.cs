namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One line of the day's list in the Stats page's right column: time, a coloured dot, a title and a length.</summary>
/// <param name="IsDrift">The dot is Drift red instead of Met.</param>
public sealed record StatsDayRow(DateTimeOffset At, string TimeText, string Title, string RightText, bool IsDrift);
