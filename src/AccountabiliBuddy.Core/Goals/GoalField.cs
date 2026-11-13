namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// The parts of a goal that merge on their own once goals sync (ADR 79, owner 2026-11-14): each has its own stamp in
/// <see cref="GoalFieldStamps"/>, so a change to one part on one device and to another part on another device both survive. Unlike a
/// task's title and note (ADR 76) no part ever ends in "Changed on both devices": the newest stamp per part wins, silently. The phone's
/// twin lists the same parts in the same order.
/// </summary>
public enum GoalField
{
    Name,

    /// <summary>The icon and the colour.</summary>
    Look,

    /// <summary>The area it sits in and its place there.</summary>
    Area,

    /// <summary>The kind with the target: a target makes no sense under another kind.</summary>
    Target,

    /// <summary>The second target; on Deep work also "Track time only" (both are its target settings, they travel together).</summary>
    SecondTarget,

    /// <summary>The whole list of source rules.</summary>
    Sources,
    Pauses,
    ShowOnToday,
    ExtraTiles,
    Notes,
}
