namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// A <see cref="TitleBarLayout"/> end child whose desired width includes room kept free on its left for something that opens there
/// (the session control's extras). In a window too narrow for everything, the layout first lets the start group give way, then
/// measures this child with less width; the child takes the cut out of this room, never out of the part that is always shown.
/// </summary>
public interface ITitleBarLeftReserve
{
    /// <summary>The reserved width at the child's left edge, already part of its desired width.</summary>
    double LeftReserve { get; }
}
