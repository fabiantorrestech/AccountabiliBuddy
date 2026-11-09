using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services;

/// <summary>A task of an ended session's checklist, with when that session started.</summary>
/// <param name="From">The PC it came from (ADR 72) when it isn't this PC's; null for this PC's own.</param>
public sealed record PastTask(SessionTask Task, DateTimeOffset SessionStart, string? From = null);
