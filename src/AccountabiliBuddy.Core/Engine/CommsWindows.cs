using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>Whether mail and chat are in their window now (comms windows, schema v6).</summary>
public static class CommsWindows
{
    /// <param name="stored">The plan as saved: whether the day has Comms parts of its own decides if mail and chat are closed outside them.</param>
    /// <param name="effective">The plan the guards read, with calendar events laid over it (ADR 58); the stored plan itself when there are none.</param>
    /// <returns>
    /// True inside any Comms part of the effective plan, one a calendar event made or not. False outside, when the stored plan
    /// has Comms parts today (as the guards see them): an event never closes or opens anything but its own time. Null when the
    /// stored plan has none (the profile decides).
    /// </returns>
    public static bool? At(DayPlan stored, DayPlan effective, DateTime localNow)
    {
        if (CommsPartsToday(effective, localNow).Any(part => part.Window.Start <= localNow && localNow < part.Window.End))
        {
            return true;
        }

        return CommsPartsToday(stored, localNow).Count > 0 ? false : null;
    }

    private static IReadOnlyList<PlannedPart> CommsPartsToday(DayPlan plan, DateTime localNow)
    {
        return DayWorkWindows.PartsIn(plan, DayTimeline.FullDay(localNow), localNow)
            .Where(part => part.Category.Behavior == CategoryBehavior.Comms)
            .ToList();
    }
}
