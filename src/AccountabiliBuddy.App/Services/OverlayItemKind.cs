namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The parts the overlay can show, in the order the user sets them. Stored as a number in the <c>overlay</c> document: never renumber, only add at the end.
/// <see cref="Ring"/> is the "Ring or timeline" row (drawn as whichever the Progress setting picks); it used to be "Ring and minutes", whose minutes are
/// now <see cref="Minutes"/>. <see cref="Timeline"/> is retired (the Progress setting decides): saved lists that hold it are read once and it never comes back.
/// </summary>
public enum OverlayItemKind
{
    Ring,
    Task,
    Timeline,
    Controls,
    TimeDate,
    Batteries,
    Sound,
    Minutes,
}
