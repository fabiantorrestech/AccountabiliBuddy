using System.Drawing;
using AccountabiliBuddy.Core.Ports;
using Windows.Win32;

namespace AccountabiliBuddy.Platform.Windows.Activity;

/// <summary>The pointer's position from GetCursorPos.</summary>
public sealed class Win32CursorPosition : ICursorPosition
{
    public (int X, int Y)? Get() => PInvoke.GetCursorPos(out Point point) ? (point.X, point.Y) : null;
}
