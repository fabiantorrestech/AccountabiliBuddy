namespace AccountabiliBuddy.Core.Ports;

/// <summary>An in-memory screenshot: 32-bit BGRA pixels, top row first, no padding between rows.</summary>
public sealed record ScreenImage(int Width, int Height, byte[] Bgra);
