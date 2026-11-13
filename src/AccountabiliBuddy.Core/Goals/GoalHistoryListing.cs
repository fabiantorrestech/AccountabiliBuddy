namespace AccountabiliBuddy.Core.Goals;

/// <summary>A goal history file as the transport lists it (GS3, GS4): which file, and its version (the store's modified time, or the file's own write time).</summary>
public sealed record GoalHistoryListing(GoalHistoryFileKey Key, DateTimeOffset ModifiedAt);
