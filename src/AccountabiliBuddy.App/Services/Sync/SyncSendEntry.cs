namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>One put of this PC's file on a sync method: the file's own time and when it reached the method (both times only, ADR 86).</summary>
/// <param name="WrittenAt">The file's <c>WrittenAt</c> (its card's time).</param>
/// <param name="ReachedAt">This PC's trusted time when the method took the file.</param>
public sealed record SyncSendEntry(DateTimeOffset WrittenAt, DateTimeOffset ReachedAt);
