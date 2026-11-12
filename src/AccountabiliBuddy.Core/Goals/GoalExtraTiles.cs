namespace AccountabiliBuddy.Core.Goals;

/// <summary>Edit goal › "Extra tiles" (change note §4a): Streak and Total live in Stats and show on the goal only when switched on.</summary>
public sealed record GoalExtraTiles(bool Streak = false, bool Total = false)
{
    public static GoalExtraTiles None { get; } = new();
}
