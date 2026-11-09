using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// A past-session task kept on this device as a local item (ADR 72): the task with where it came from. Only its keeper holds
/// it (sealed, never logged); every other device knows it only as a stub.
/// </summary>
/// <param name="SourceDeviceId">The PC whose history it came from.</param>
/// <param name="SourceName">That PC's name, for "from Work PC".</param>
/// <param name="SessionStart">When its session started; null for one ticked outside a session.</param>
/// <param name="EditedAt">When it was last changed.</param>
public sealed record SyncKeptPast(Guid SourceDeviceId, string SourceName, DateTimeOffset? SessionStart, SessionTask Task, DateTimeOffset EditedAt);
