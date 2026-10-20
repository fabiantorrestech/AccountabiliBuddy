namespace AccountabiliBuddy.Core.Domain;

/// <summary>A rectangle on the virtual desktop in physical pixels (right and bottom excluded).</summary>
public readonly record struct ScreenBox(int Left, int Top, int Right, int Bottom);
