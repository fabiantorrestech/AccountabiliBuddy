namespace AccountabiliBuddy.Core.Engine;

/// <summary>One thing the Plan helper noticed: what to tell the user, the date it is about and, sometimes, the plan edit that would fix it.</summary>
/// <param name="Date">The day the suggestion is about (today for a goal that doesn't fit).</param>
/// <param name="Text">A sentence for the user, in 12-hour times.</param>
/// <param name="Edit">The plan edit "Move it" would make; null when there is nothing to change in the plan.</param>
public sealed record PlanSuggestion(PlanSuggestionKind Kind, DateOnly Date, string Text, PlanEdit? Edit = null)
{
    /// <summary>What "Leave it" remembers: the kind and the day, so the same suggestion for another day still shows.</summary>
    public string Key => $"{Kind}:{Date:yyyy-MM-dd}";
}
