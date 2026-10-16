using System.Globalization;
using AccountabiliBuddy.Core.Ports;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace AccountabiliBuddy.Platform.Windows.Enforcement;

/// <summary>
/// Minimizes windows with ShowWindowAsync, which posts the request instead of waiting,
/// so a hung app cannot freeze the caller.
/// Windows of elevated (admin) apps cannot be minimized from a normal process.
/// </summary>
public sealed class WindowsEnforcer : IEnforcer
{
    public Task MinimizeWindowAsync(string windowId, CancellationToken cancellationToken = default)
    {
        // Window ids are the handle in hex (see Activity.WindowIds).
        if (nint.TryParse(windowId, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out nint handle))
        {
            var window = new HWND(handle);
            if (PInvoke.IsWindow(window))
            {
                PInvoke.ShowWindowAsync(window, SHOW_WINDOW_CMD.SW_MINIMIZE);
            }
        }

        return Task.CompletedTask;
    }
}
