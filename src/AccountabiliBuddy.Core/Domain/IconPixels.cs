namespace AccountabiliBuddy.Core.Domain;

/// <summary>An icon as raw pixels: 4 bytes per pixel in blue, green, red, alpha order, top row first.</summary>
public sealed record IconPixels(int Width, int Height, byte[] Bgra);
