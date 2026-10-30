using System.Runtime.InteropServices;

namespace AccountabiliBuddy.Platform.Windows.Taskbar;

/// <summary>
/// <c>SHAppBarMessage</c> by hand: CsWin32 only generates its struct for one CPU, and this project builds for AnyCPU. The struct
/// has the same natural layout on 32 and 64 bit.
/// </summary>
internal static class AppBarInterop
{
    public const uint New = 0x0;
    public const uint Remove = 0x1;
    public const uint GetState = 0x4;
    public const uint GetTaskbarPos = 0x5;
    public const uint AutoHideState = 0x1;
    public const uint EdgeLeft = 0;
    public const uint EdgeTop = 1;
    public const uint EdgeRight = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct AppBarData
    {
        public uint CbSize;
        public nint Window;
        public uint CallbackMessage;
        public uint Edge;
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public nint Param;
    }

    [DllImport("shell32.dll")]
    public static extern nuint SHAppBarMessage(uint message, ref AppBarData data);
}
