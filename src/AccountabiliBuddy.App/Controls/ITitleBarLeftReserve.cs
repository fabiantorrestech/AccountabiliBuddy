namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// A <see cref="TitleBarLayout"/> centre child whose desired width includes room kept free on its left for something that opens
/// there (the session control's extras). The layout centres the rest of the width and keeps this room clear of the left group.
/// </summary>
public interface ITitleBarLeftReserve
{
    /// <summary>The reserved width at the child's left edge, already part of its desired width.</summary>
    double LeftReserve { get; }
}
