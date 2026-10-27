namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// One of the user's Beeminder goals (ADR 43), as the PC's sealed sync file carries it so the phone's Today shows the
/// same card without a token or a connection of its own. Derived only: never the token or anything else Beeminder sent.
/// </summary>
/// <param name="Title">The goal's title, or its slug when it has none.</param>
/// <param name="Needed">What's needed to stay on track, in Beeminder's words (e.g. "+2 within 1 day").</param>
/// <param name="SafeDays">Days of buffer when the PC last asked; 0 or less = due by the next deadline.</param>
/// <param name="DueAt">When the goal derails without a data point.</param>
public sealed record SyncBeeminderGoal(string Title, string Needed, int SafeDays, DateTimeOffset DueAt);
