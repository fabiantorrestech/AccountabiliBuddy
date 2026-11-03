using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>Whether mail and chat are in their window now (comms windows, schema v6).</summary>
public static class CommsWindows
{
    /// <returns>
    /// True inside any Comms part, one a calendar event made (ADR 58) or not. False outside, when the plan itself has Comms
    /// parts today: an event's Comms part never closes comms elsewhere. Null when it has none, or only event-made ones and
    /// now is outside them (the profile decides).
    /// </returns>
    public static bool? At(DayPlan plan, DateTime localNow)
    {
        var comms = DayWorkWindows.PartsIn(plan, DayTimeline.FullDay(localNow), localNow)
            .Where(part => part.Category.Behavior == CategoryBehavior.Comms)
            .ToList();
        if (comms.Any(part => part.Window.Start <= localNow && localNow < part.Window.End))
        {
            return true;
        }

        return comms.Any(part => !part.Part.FromEvent) ? false : null;
    }
}
