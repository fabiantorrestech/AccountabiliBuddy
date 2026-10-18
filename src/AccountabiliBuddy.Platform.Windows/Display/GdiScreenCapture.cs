using AccountabiliBuddy.Core.Ports;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace AccountabiliBuddy.Platform.Windows.Display;

/// <summary>
/// Screen copy with classic GDI (BitBlt from the desktop). Fast enough for a one-off backdrop snapshot.
/// DRM-protected video shows up black, which is fine for a blurred backdrop.
/// </summary>
public sealed class GdiScreenCapture : IScreenCapture
{
    public ScreenImage? Capture(PixelBox area)
    {
        if (area.Width <= 0 || area.Height <= 0)
        {
            return null;
        }

        HDC screen = PInvoke.GetDC(HWND.Null);
        HDC memory = PInvoke.CreateCompatibleDC(screen);
        HBITMAP bitmap = PInvoke.CreateCompatibleBitmap(screen, area.Width, area.Height);
        HGDIOBJ previous = PInvoke.SelectObject(memory, bitmap);

        try
        {
            return PInvoke.BitBlt(memory, 0, 0, area.Width, area.Height, screen, area.Left, area.Top,
                    ROP_CODE.SRCCOPY | ROP_CODE.CAPTUREBLT)
                ? GdiPixels.Read(memory, bitmap, area.Width, area.Height)
                : null;
        }
        finally
        {
            PInvoke.SelectObject(memory, previous);
            PInvoke.DeleteObject(bitmap);
            PInvoke.DeleteDC(memory);
            PInvoke.ReleaseDC(HWND.Null, screen);
        }
    }
}
