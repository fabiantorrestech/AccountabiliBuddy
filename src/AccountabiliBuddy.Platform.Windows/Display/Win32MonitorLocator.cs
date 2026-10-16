using AccountabiliBuddy.Core.Ports;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace AccountabiliBuddy.Platform.Windows.Display;

/// <summary>Looks up a monitor by its device name ("\\.\DISPLAY1"), the id used in activity snapshots.</summary>
public sealed class Win32MonitorLocator : IMonitorLocator
{
    public unsafe MonitorArea? Find(string monitorId)
    {
        MonitorArea? found = null;

        PInvoke.EnumDisplayMonitors(default, (RECT?)null, (monitor, _, _, _) =>
        {
            var info = new MONITORINFOEXW();
            info.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);

            if (PInvoke.GetMonitorInfo(monitor, (MONITORINFO*)&info)
                && string.Equals(info.szDevice.ToString(), monitorId, StringComparison.Ordinal))
            {
                found = new MonitorArea(ToBox(info.monitorInfo.rcMonitor), ToBox(info.monitorInfo.rcWork));
                return false; // stop enumerating
            }

            return true;
        }, default);

        return found;
    }

    private static PixelBox ToBox(RECT rect)
    {
        return new PixelBox(rect.left, rect.top, rect.right, rect.bottom);
    }
}
