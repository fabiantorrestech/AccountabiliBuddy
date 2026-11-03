namespace AccountabiliBuddy.Core.Domain;

/// <summary>The quit safeguard is holding now (ADR 59).</summary>
/// <param name="CategoryName">The day plan category it holds for; null unless <paramref name="Reason"/> is <see cref="QuitLockReason.Category"/>.</param>
/// <param name="Until">When that part of the day ends (local time), past any ticked parts right after it; null when no end is known.</param>
public sealed record QuitLock(QuitLockReason Reason, string? CategoryName = null, DateTime? Until = null);
