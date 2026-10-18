using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services;

/// <summary>The session as last saved to disk, and when the app was last known to be running.</summary>
public sealed record SavedSession(SessionCheckpoint Checkpoint, DateTimeOffset LastAliveAt);
