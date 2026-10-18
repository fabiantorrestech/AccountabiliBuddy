using AccountabiliBuddy.Core.Ports;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace AccountabiliBuddy.Platform.Windows.Activity;

/// <summary>Idle time from GetLastInputInfo: time since the last keyboard or mouse input in this sign-in session.</summary>
public sealed class Win32UserIdleDetector : IUserIdleDetector
{
    public unsafe TimeSpan GetIdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)sizeof(LASTINPUTINFO) };
        if (!PInvoke.GetLastInputInfo(ref info))
        {
            return TimeSpan.Zero;
        }

        // Both are 32-bit millisecond tick counts; unsigned subtraction handles the 49-day wrap-around.
        uint elapsed = unchecked((uint)Environment.TickCount - info.dwTime);
        return TimeSpan.FromMilliseconds(elapsed);
    }
}
