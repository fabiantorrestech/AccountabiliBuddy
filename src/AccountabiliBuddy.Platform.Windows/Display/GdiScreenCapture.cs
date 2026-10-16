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
    public unsafe ScreenImage? Capture(PixelBox area)
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
            if (!PInvoke.BitBlt(memory, 0, 0, area.Width, area.Height, screen, area.Left, area.Top,
                    ROP_CODE.SRCCOPY | ROP_CODE.CAPTUREBLT))
            {
                return null;
            }

            var info = new BITMAPINFO();
            info.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
            info.bmiHeader.biWidth = area.Width;
            info.bmiHeader.biHeight = -area.Height; // negative = top-down rows
            info.bmiHeader.biPlanes = 1;
            info.bmiHeader.biBitCount = 32;
            info.bmiHeader.biCompression = 0; // BI_RGB

            byte[] pixels = new byte[area.Width * area.Height * 4];
            fixed (byte* buffer = pixels)
            {
                int rows = PInvoke.GetDIBits(memory, bitmap, 0, (uint)area.Height, buffer, &info, DIB_USAGE.DIB_RGB_COLORS);
                if (rows == 0)
                {
                    return null;
                }
            }

            return new ScreenImage(area.Width, area.Height, pixels);
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
