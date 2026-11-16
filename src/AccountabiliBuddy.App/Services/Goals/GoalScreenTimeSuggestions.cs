using AccountabiliBuddy.Core.Goals;

namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>
/// ST4 "From your goals" (ADR 79 amendment, owner answer 2026-11-15): the apps and sites that goals' screen-time rules already list. They travel
/// sealed to the user's own devices already, so offering them in the editor reads nothing new and sends nothing: it is how an entry for another
/// device (a phone's package id, say) can be picked on this PC without a synced list of what each device has seen.
/// </summary>
public static class GoalScreenTimeSuggestions
{
    /// <summary>The distinct entries (one per kind and text, in the goals' order) of every screen-time rule of <paramref name="goals"/>; without their device lists.</summary>
    public static IReadOnlyList<GoalAppMatch> EntriesOf(IEnumerable<Goal> goals)
    {
        var found = new List<GoalAppMatch>();
        foreach (GoalAppMatch match in goals.Where(goal => !goal.IsDeepWork)
                     .SelectMany(goal => goal.Sources)
                     .Where(rule => rule.Type == GoalSourceType.ScreenTime)
                     .SelectMany(rule => rule.Apps ?? []))
        {
            if (!string.IsNullOrWhiteSpace(match.Text) && !found.Any(held => held.Kind == match.Kind && string.Equals(held.Text, match.Text, StringComparison.OrdinalIgnoreCase)))
            {
                found.Add(new GoalAppMatch(match.Kind, match.Text));
            }
        }

        return found;
    }
}
