using AccountabiliBuddy.Core.Ports;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;

namespace AccountabiliBuddy.Platform.Windows.Display;

/// <summary>Reads the pixels of a GDI bitmap into a top-down 32-bit BGRA buffer.</summary>
internal static class GdiPixels
{
    public static unsafe ScreenImage? Read(HDC memory, HBITMAP bitmap, int width, int height)
    {
        var info = new BITMAPINFO();
        info.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
        info.bmiHeader.biWidth = width;
        info.bmiHeader.biHeight = -height; // negative = top-down rows
        info.bmiHeader.biPlanes = 1;
        info.bmiHeader.biBitCount = 32;
        info.bmiHeader.biCompression = 0; // BI_RGB

        byte[] pixels = new byte[width * height * 4];
        fixed (byte* buffer = pixels)
        {
            int rows = PInvoke.GetDIBits(memory, bitmap, 0, (uint)height, buffer, &info, DIB_USAGE.DIB_RGB_COLORS);
            if (rows == 0)
            {
                return null;
            }
        }

        // GDI leaves the alpha byte at 0; make the image opaque so consumers can treat it as premultiplied BGRA.
        for (int index = 3; index < pixels.Length; index += 4)
        {
            pixels[index] = 255;
        }

        return new ScreenImage(width, height, pixels);
    }

    /// <summary>True when every sampled pixel has the same colour, e.g. a window that rendered nothing (all black).</summary>
    public static bool LooksBlank(ScreenImage image)
    {
        const int samples = 64;
        int pixelCount = image.Width * image.Height;
        if (pixelCount == 0)
        {
            return true;
        }

        int first = BitConverter.ToInt32(image.Bgra, 0);
        for (int sample = 1; sample < samples; sample++)
        {
            int offset = (int)((long)pixelCount * sample / samples) * 4;
            if (BitConverter.ToInt32(image.Bgra, offset) != first)
            {
                return false;
            }
        }

        return true;
    }
}
