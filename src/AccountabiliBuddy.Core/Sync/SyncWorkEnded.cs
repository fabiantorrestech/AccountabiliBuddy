namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// Work ended early today (ADR 65): a PC's Start shutdown during a work part, after the wait and the reason. Times only, never the
/// reason. Every device takes the newest edit for the plan date, like lunch; an empty <paramref name="Windows"/> with a newer
/// <paramref name="EditedAt"/> means it was undone ("Back to work").
/// </summary>
/// <param name="Date">The plan date it belongs to (today's only).</param>
/// <param name="Windows">Each ended stretch, from the moment it was confirmed to the part's planned end.</param>
/// <param name="EditedAt">When it was last ended or undone.</param>
public sealed record SyncWorkEnded(DateOnly Date, IReadOnlyList<SyncWorkEndedWindow> Windows, DateTimeOffset EditedAt);
