using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services;

/// <summary>What a stretch with the PC off or the app closed did to the session (ADR 60).</summary>
/// <param name="Saved">The saved session it came back from; null for a gap the running app noticed itself (the PC slept).</param>
public sealed record SessionGapReport(TimeSpan Gap, SessionGapOutcome Outcome, DateTimeOffset LastAliveAt, DateTimeOffset? OriginalEnd, SavedSession? Saved);
