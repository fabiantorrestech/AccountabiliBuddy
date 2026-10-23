namespace AccountabiliBuddy.App.Services;

/// <summary>How much focus a day the user aims for (document "goal"; asked on the first run, read by the goal unlock later).</summary>
/// <param name="DailyMinutes">Minutes of counted focus a day; 60 by default.</param>
public sealed record GoalSettings(int DailyMinutes)
{
    public static GoalSettings Default { get; } = new(60);
}
