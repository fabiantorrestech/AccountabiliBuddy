namespace AccountabiliBuddy.App.Services.Beeminder;

/// <summary>
/// One of the user's Beeminder goals as the app keeps it (ADR 43): only what Today shows.
/// </summary>
/// <param name="Title">The goal's title, or its slug when it has none.</param>
/// <param name="Needed">What's needed to stay on track, in Beeminder's words (e.g. "+2 within 1 day"); may be empty.</param>
/// <param name="SafeDays">Days of buffer when last read; 0 or less = due by the next deadline.</param>
/// <param name="DueAt">When the goal derails without a data point (Beeminder's "losedate").</param>
public sealed record BeeminderGoal(string Title, string Needed, int SafeDays, DateTimeOffset DueAt);
