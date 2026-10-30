using AccountabiliBuddy.Core.Ports;
using Avalonia;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Positions toasts, prompts and overlays on a specific monitor.
/// Monitor areas are in physical pixels; Avalonia sizes are in device-independent units, so sizes are
/// converted with the window's own scaling once it is open on the target monitor.
/// </summary>
public static class MonitorPlacement
{
    private const int CornerMarginPixels = 16;

    // Device-independent units; Windows 11's taskbar is 48 tall, plus a small gap.
    private const double AutoHideTaskbarHeight = 64;

    public enum Corner
    {
        TopRight,
        BottomRight,
    }

    /// <summary>Keeps the window in a corner of the monitor's work area, also when its content changes size.</summary>
    public static void PlaceInCorner(Window window, MonitorArea area, Corner corner)
    {
        // Start on the target monitor so the window gets that monitor's DPI from the beginning.
        window.Position = new PixelPoint(area.WorkArea.Left + CornerMarginPixels, area.WorkArea.Top + CornerMarginPixels);

        window.SizeChanged += (_, _) => MoveToCorner(window, area, corner);
        window.Opened += (_, _) => MoveToCorner(window, area, corner);
    }

    /// <summary>Centres the window in the monitor's work area, also when its content changes size.</summary>
    public static void Center(Window window, MonitorArea area)
    {
        window.Position = new PixelPoint(area.WorkArea.Left + CornerMarginPixels, area.WorkArea.Top + CornerMarginPixels);

        void MoveToCenter()
        {
            double scaling = window.DesktopScaling;
            int widthPixels = (int)Math.Ceiling(window.Bounds.Width * scaling);
            int heightPixels = (int)Math.Ceiling(window.Bounds.Height * scaling);
            window.Position = new PixelPoint(
                area.WorkArea.Left + ((area.WorkArea.Width - widthPixels) / 2),
                area.WorkArea.Top + ((area.WorkArea.Height - heightPixels) / 2));
        }

        window.SizeChanged += (_, _) => MoveToCenter();
        window.Opened += (_, _) => MoveToCenter();
    }

    /// <summary>Covers the whole monitor, including the taskbar.</summary>
    public static void Cover(Window window, MonitorArea area)
    {
        window.Position = new PixelPoint(area.Bounds.Left, area.Bounds.Top);
        window.Width = 200;
        window.Height = 200;

        window.Opened += (_, _) =>
        {
            double scaling = window.DesktopScaling;
            window.Width = area.Bounds.Width / scaling;
            window.Height = area.Bounds.Height / scaling;
            window.Position = new PixelPoint(area.Bounds.Left, area.Bounds.Top);
        };
    }

    /// <summary>Puts the window just above a box (the docked overlay), its right edge on the box's right edge or its left on the left, kept on its monitor.</summary>
    public static void PlaceAbove(Window window, PixelBox anchor, bool alignRight)
    {
        window.Position = new PixelPoint(anchor.Left, anchor.Top - 1);

        void Move()
        {
            double scaling = window.DesktopScaling;
            int width = (int)Math.Ceiling(window.Bounds.Width * scaling);
            int height = (int)Math.Ceiling(window.Bounds.Height * scaling);
            int gap = (int)(6 * scaling);
            window.Position = new PixelPoint(alignRight ? anchor.Right - width : anchor.Left, Math.Max(0, anchor.Top - gap - height));
        }

        window.SizeChanged += (_, _) => Move();
        window.Opened += (_, _) => Move();
    }

    private static void MoveToCorner(Window window, MonitorArea area, Corner corner)
    {
        double scaling = window.DesktopScaling;
        int widthPixels = (int)Math.Ceiling(window.Bounds.Width * scaling);
        int heightPixels = (int)Math.Ceiling(window.Bounds.Height * scaling);

        // With an auto-hiding taskbar the work area reaches the screen edge, but the taskbar (and its clock)
        // still pops up over it, so keep clear of the bottom edge by roughly a taskbar's height.
        int bottomMargin = area.WorkArea.Bottom == area.Bounds.Bottom
            ? (int)Math.Ceiling(AutoHideTaskbarHeight * scaling)
            : CornerMarginPixels;

        int x = area.WorkArea.Right - widthPixels - CornerMarginPixels;
        int y = corner == Corner.TopRight
            ? area.WorkArea.Top + CornerMarginPixels
            : area.WorkArea.Bottom - heightPixels - bottomMargin;

        window.Position = new PixelPoint(x, y);
    }
}
