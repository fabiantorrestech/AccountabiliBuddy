using System.Globalization;
using Windows.Win32.Foundation;

namespace AccountabiliBuddy.Platform.Windows.Activity;

/// <summary>Converts between window handles and the opaque <c>WindowId</c> strings used by Core.</summary>
internal static class WindowIds
{
    public static string Format(HWND window)
    {
        return ((nint)window).ToString("X");
    }

    public static bool TryParse(string windowId, out HWND window)
    {
        window = HWND.Null;
        if (!nint.TryParse(windowId, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out nint handle) || handle == 0)
        {
            return false;
        }

        window = new HWND(handle);
        return true;
    }
}
