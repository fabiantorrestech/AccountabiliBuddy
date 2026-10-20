using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace AccountabiliBuddy.Platform.Windows.Display;

/// <summary>
/// Reads an app's icon from its program file: finds a running process by name, takes the file's first large
/// (32 px) icon, and copies its pixels. Apps the current user can't inspect (e.g. running as administrator)
/// give no icon.
/// </summary>
public sealed class Win32AppIconReader : IAppIconReader
{
    public IconPixels? ReadIcon(string processName)
    {
        string? path = Win32DesktopShell.RunningProgramFile(processName);
        return path is null ? null : ReadFileIcon(path);
    }

    private static unsafe IconPixels? ReadFileIcon(string path)
    {
        HICON large;
        uint extracted;
        fixed (char* file = path)
        {
            extracted = PInvoke.ExtractIconEx(new PCWSTR(file), 0, &large, null, 1);
        }

        if (extracted == 0 || extracted == uint.MaxValue || large.IsNull)
        {
            return null;
        }

        try
        {
            return CopyPixels(large);
        }
        finally
        {
            PInvoke.DestroyIcon(large);
        }
    }

    private static unsafe IconPixels? CopyPixels(HICON icon)
    {
        ICONINFO iconInfo;
        if (!PInvoke.GetIconInfo(icon, &iconInfo))
        {
            return null;
        }

        try
        {
            if (iconInfo.hbmColor.IsNull)
            {
                return null; // Monochrome icon; not worth showing.
            }

            BITMAP bitmap;
            if (PInvoke.GetObject(iconInfo.hbmColor, sizeof(BITMAP), &bitmap) == 0)
            {
                return null;
            }

            int width = bitmap.bmWidth;
            int height = bitmap.bmHeight;
            var header = new BITMAPINFO();
            header.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
            header.bmiHeader.biWidth = width;
            header.bmiHeader.biHeight = -height; // Top row first.
            header.bmiHeader.biPlanes = 1;
            header.bmiHeader.biBitCount = 32;
            header.bmiHeader.biCompression = 0; // BI_RGB

            byte[] pixels = new byte[width * height * 4];
            HDC screen = PInvoke.GetDC(HWND.Null);
            try
            {
                fixed (byte* bits = pixels)
                {
                    if (PInvoke.GetDIBits(screen, iconInfo.hbmColor, 0, (uint)height, bits, &header, DIB_USAGE.DIB_RGB_COLORS) == 0)
                    {
                        return null;
                    }
                }
            }
            finally
            {
                _ = PInvoke.ReleaseDC(HWND.Null, screen);
            }

            // Old icons have no alpha channel at all: make them opaque.
            bool hasAlpha = false;
            for (int i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] != 0)
                {
                    hasAlpha = true;
                    break;
                }
            }

            if (!hasAlpha)
            {
                for (int i = 3; i < pixels.Length; i += 4)
                {
                    pixels[i] = 255;
                }
            }

            return new IconPixels(width, height, pixels);
        }
        finally
        {
            if (!iconInfo.hbmColor.IsNull)
            {
                PInvoke.DeleteObject(iconInfo.hbmColor);
            }

            if (!iconInfo.hbmMask.IsNull)
            {
                PInvoke.DeleteObject(iconInfo.hbmMask);
            }
        }
    }
}
