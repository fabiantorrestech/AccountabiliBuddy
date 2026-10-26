namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// What is going on now, for holding Cold Turkey's keys (ADR 39): a focus session the user started (and its
/// profile's expected blocks), Pre-Work guarding, work hours guarding, and the day plan categories of now.
/// </summary>
public sealed record HoldWindows(
    bool FocusSession,
    IReadOnlyCollection<string> SessionBlocks,
    bool PreWork,
    bool WorkHours,
    IReadOnlyCollection<Guid> Categories)
{
    public static HoldWindows None { get; } = new(false, Array.Empty<string>(), false, false, Array.Empty<Guid>());
}
