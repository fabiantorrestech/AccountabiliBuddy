namespace AccountabiliBuddy.Core.Engine;

/// <summary>Today's deep work against the daily goal (document "goal").</summary>
/// <param name="Focused">Today's counted focus across the user's devices.</param>
/// <param name="GoalMinutes">The daily goal in minutes.</param>
public sealed record GoalProgress(TimeSpan Focused, int GoalMinutes)
{
    public TimeSpan Goal => TimeSpan.FromMinutes(Math.Max(1, GoalMinutes));

    public bool Met => Focused >= Goal;

    /// <summary>0 to 1, for a bar or ring.</summary>
    public double Fraction => Math.Clamp(Focused / Goal, 0, 1);

    /// <summary>"40m of 1h", then "Goal met · 1h 05m".</summary>
    public string Text => Met ? $"Goal met · {Duration(Focused)}" : $"{Duration(Focused)} of {Duration(Goal)}";

    /// <summary>"40m", "1h", "1h 30m".</summary>
    public static string Duration(TimeSpan time)
    {
        int hours = (int)time.TotalHours;
        int minutes = time.Minutes;
        if (hours == 0)
        {
            return $"{minutes}m";
        }

        return minutes == 0 ? $"{hours}h" : $"{hours}h {minutes}m";
    }
}
