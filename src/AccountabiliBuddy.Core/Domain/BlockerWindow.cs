namespace AccountabiliBuddy.Core.Domain;

/// <summary>A stretch of local time in which a blocker's block (e.g. one of Cold Turkey's) blocks.</summary>
/// <param name="Name">The block's name, as the blocker calls it.</param>
/// <param name="Window">Its real times (it may run past the stretch asked about); for an always-on block, that stretch.</param>
/// <param name="AlwaysOn">True for a switched-on block with no schedule.</param>
public sealed record BlockerWindow(string Name, LocalWindow Window, bool AlwaysOn = false);
