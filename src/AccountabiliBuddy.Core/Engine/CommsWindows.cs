using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>Whether mail and chat are in their window now (comms windows, schema v6).</summary>
public static class CommsWindows
{
    /// <returns>Null when today's plan has no Comms parts (the profile decides); true inside one; false outside.</returns>
    public static bool? At(DayPlan plan, DateTime localNow)
    {
        var comms = DayWorkWindows.PartsIn(plan, DayTimeline.FullDay(localNow), localNow)
            .Where(part => part.Category.Behavior == CategoryBehavior.Comms)
            .ToList();
        if (comms.Count == 0)
        {
            return null;
        }

        return comms.Any(part => part.Window.Start <= localNow && localNow < part.Window.End);
    }
}
