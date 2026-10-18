using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Platform.Windows.Display;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.Storage.Xps;

namespace AccountabiliBuddy.Platform.Windows.Ocr;

/// <summary>
/// Copies one window's pixels for OCR. PrintWindow asks the window to draw itself, so the copy is not spoiled by
/// toasts or prompts lying on top of it and Windows draws no "being captured" border (unlike
/// Windows.Graphics.Capture). Some apps draw nothing that way; then the visible screen area of the window is copied.
/// </summary>
internal static class WindowCapture
{
    // Renders DirectComposition/GPU content too (Chromium, UWP); not in older SDK headers.
    private const PRINT_WINDOW_FLAGS RenderFullContent = (PRINT_WINDOW_FLAGS)0x00000002;

    public static ScreenImage? Capture(HWND window)
    {
        if (!PInvoke.IsWindow(window) || !PInvoke.GetWindowRect(window, out RECT bounds))
        {
            return null;
        }

        int width = bounds.right - bounds.left;
        int height = bounds.bottom - bounds.top;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        ScreenImage? printed = PrintWindowToImage(window, width, height);
        if (printed is not null && !GdiPixels.LooksBlank(printed))
        {
            return printed;
        }

        return new GdiScreenCapture().Capture(new PixelBox(bounds.left, bounds.top, bounds.right, bounds.bottom));
    }

    private static ScreenImage? PrintWindowToImage(HWND window, int width, int height)
    {
        HDC screen = PInvoke.GetDC(HWND.Null);
        HDC memory = PInvoke.CreateCompatibleDC(screen);
        HBITMAP bitmap = PInvoke.CreateCompatibleBitmap(screen, width, height);
        HGDIOBJ previous = PInvoke.SelectObject(memory, bitmap);

        try
        {
            return PInvoke.PrintWindow(window, memory, RenderFullContent)
                ? GdiPixels.Read(memory, bitmap, width, height)
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
