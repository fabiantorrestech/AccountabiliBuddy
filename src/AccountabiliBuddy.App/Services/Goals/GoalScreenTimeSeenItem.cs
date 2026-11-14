using AccountabiliBuddy.Core.Goals;

namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>One program or site on the screen-time pick list: what a tap adds (<paramref name="Match"/>), the name this PC shows for it and its time in the last 30 days.</summary>
public sealed class GoalScreenTimeSeenItem
{
    public GoalScreenTimeSeenItem(GoalAppMatch match, string name, bool isSite, double minutes)
    {
        Match = match;
        Name = name;
        IsSite = isSite;
        Minutes = minutes;
    }

    public GoalAppMatch Match { get; }

    /// <summary>The name shown: the user's own name for it (History rename), else the real one.</summary>
    public string Name { get; }

    public bool IsSite { get; }

    public double Minutes { get; }

    /// <summary>The real name when it differs from the shown one ("chrome"), else null.</summary>
    public string? RealName => string.Equals(Name, Match.Text, StringComparison.OrdinalIgnoreCase) ? null : Match.Text;
}
