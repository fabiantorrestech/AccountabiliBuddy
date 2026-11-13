namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>One task "Changed on both devices" in the conflicts list (SYNC1b). The title is the user's own text: shown on screen only, never logged or exported.</summary>
/// <param name="Id">The task's id.</param>
/// <param name="Title">The task's title as the list shows it.</param>
/// <param name="Count">How many of its fields (title, note) wait for an answer.</param>
public sealed record SyncConflictItem(Guid Id, string Title, int Count);
