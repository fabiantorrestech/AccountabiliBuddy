namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>A file in a goal history transport's listing (ADR 79 shared history, GS3b).</summary>
/// <param name="Name">The opaque file name (<see cref="GoalHistoryNames"/>).</param>
/// <param name="ModifiedAt">The store's version of the file: Drive's modified time, the relay's stored time.</param>
/// <param name="Size">Its size in bytes.</param>
/// <param name="Tag">A content tag when the store gives one (the relay's etag); a file whose tag is the one already merged is not downloaded again; null when none.</param>
public sealed record GoalHistoryRemoteFile(string Name, DateTimeOffset ModifiedAt, long Size, string? Tag);
