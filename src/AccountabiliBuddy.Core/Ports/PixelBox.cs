namespace AccountabiliBuddy.Core.Ports;

/// <summary>A rectangle in physical pixels.</summary>
public sealed record PixelBox(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;
}
