using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services;

/// <summary>A task of another PC's history (ADR 72), with the PC it came from.</summary>
/// <param name="SessionStart">When its session started; null for one ticked outside a session.</param>
public sealed record ReceivedPastTask(Guid DeviceId, string DeviceName, DateTimeOffset? SessionStart, bool Outside, SessionTask Task);
