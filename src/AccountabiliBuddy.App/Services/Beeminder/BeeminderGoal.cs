namespace AccountabiliBuddy.App.Services.Beeminder;

/// <summary>
/// One of the user's Beeminder goals as the app keeps it (ADR 43): only what Today shows.
/// </summary>
/// <param name="Title">The goal's title, or its slug when it has none.</param>
/// <param name="Needed">What's needed to stay on track, in Beeminder's words (e.g. "+2 within 1 day"); may be empty.</param>
/// <param name="SafeDays">Days of buffer when last read; 0 or less = due by the next deadline.</param>
/// <param name="DueAt">When the goal derails without a data point (Beeminder's "losedate").</param>
/// <param name="Slug">Beeminder's own name for the goal (the address part, ADR 84); empty in answers kept before ADR 84.</param>
/// <param name="AggDay">How Beeminder turns a day's datapoints into one value ("sum", "last", "max", ...; ADR 84); empty = sum.</param>
public sealed record BeeminderGoal(string Title, string Needed, int SafeDays, DateTimeOffset DueAt, string Slug = "", string AggDay = "")
{
    /// <summary>What a goal source stores to point at this goal: the slug, or the title for an answer kept before ADR 84.</summary>
    public string Key => Slug.Length > 0 ? Slug : Title;
}
