using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services;

/// <summary>A session the user ended by hand.</summary>
/// <param name="Ended">The state just before it ended.</param>
/// <param name="EarlyEndReason">The friction reason when a focus phase was ended early; null otherwise.</param>
public sealed record SessionEnd(SessionState Ended, string? EarlyEndReason);
