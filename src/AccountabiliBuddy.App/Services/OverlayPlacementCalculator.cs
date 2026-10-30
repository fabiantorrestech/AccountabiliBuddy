using AccountabiliBuddy.Core.Ports;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Decides where the overlay goes from the settings and what <see cref="ITaskbarGeometry"/> reports (ADR 51). It keeps every guess about
/// Windows' layout in one place: when the free stretch of the taskbar is unknown, the tile goes right against the tray, which is the one
/// edge that is always known. Pure: no windows, nothing read from the machine.
/// </summary>
public static class OverlayPlacementCalculator
{
    // Device-independent pixels, used where the taskbar can't tell: the tray's usual width, and room for a tile left of it.
    private const double FallbackTrayDips = 300;
    private const double FallbackTileDips = 360;
    private const double GapDips = 8;
    private const double SideGapDips = 4;

    public static OverlayTarget Resolve(OverlaySettings settings, IReadOnlyList<TaskbarInfo> taskbars)
    {
        TaskbarInfo? taskbar = taskbars.FirstOrDefault(info => info.IsPrimary) ?? taskbars.FirstOrDefault();
        OverlayPlacement placement = settings.EffectivePlacement;
        if (taskbar is null)
        {
            // Explorer is restarting or has no taskbar: float at the bottom right of the main monitor until one is back.
            return new OverlayTarget(OverlayTargetKind.Floating, null, 1, new PixelBox(0, 0, 0, 0), true, 0, false);
        }

        if (taskbar.IsVertical)
        {
            return new OverlayTarget(OverlayTargetKind.VerticalTile, taskbar.MonitorId, taskbar.Scale, SideBand(taskbar), taskbar.Edge == TaskbarEdge.Right,
                0, false, taskbar.Edge);
        }

        if (taskbar.AutoHide && !taskbar.IsShown)
        {
            return settings.AutoHide switch
            {
                AutoHideMode.SlimLine => new OverlayTarget(OverlayTargetKind.SlimLine, taskbar.MonitorId, taskbar.Scale, taskbar.Monitor, true, 0, false),
                AutoHideMode.HideWithIt => OverlayTarget.Hidden,
                _ => Floating(taskbar, alignRight: true),
            };
        }

        if (placement is OverlayPlacement.FloatBottomRight or OverlayPlacement.FloatBottomLeft)
        {
            return Floating(taskbar, placement == OverlayPlacement.FloatBottomRight);
        }

        return Docked(settings, placement, taskbar);
    }

    private static OverlayTarget Docked(OverlaySettings settings, OverlayPlacement placement, TaskbarInfo taskbar)
    {
        PixelBox bar = taskbar.Bounds;
        double scale = taskbar.Scale;
        int trayLeft = taskbar.TrayBounds?.Left ?? bar.Right - (int)(FallbackTrayDips * scale);
        int gap = (int)(GapDips * scale);
        int height = bar.Height;

        PixelBox band;
        bool alignRight = true;
        bool coversTray = false;
        switch (placement)
        {
            case OverlayPlacement.CoverTray:
                band = new PixelBox(taskbar.FreeArea?.Left ?? trayLeft - (int)(FallbackTileDips * scale), bar.Top, bar.Right, bar.Bottom);
                coversTray = true;
                break;

            case OverlayPlacement.TaskbarLeft when taskbar.LeftFreeArea is { } leftFree:
                band = new PixelBox(bar.Left, bar.Top, leftFree.Right - gap, bar.Bottom);
                alignRight = false;
                break;

            case OverlayPlacement.BesideTray:
                band = new PixelBox(trayLeft - (int)(FallbackTileDips * scale), bar.Top, trayLeft, bar.Bottom);
                break;

            default:
                // Over the taskbar. Icons that start at the left can be followed (the tile sits right after the last one); otherwise it hugs the tray.
                if (taskbar.IconsLeftAligned && settings.LeftAligned == LeftAlignedMode.FollowApps && taskbar.FreeArea is { } followed)
                {
                    band = new PixelBox(followed.Left + gap, bar.Top, trayLeft, bar.Bottom);
                    alignRight = false;
                }
                else
                {
                    int left = taskbar.FreeArea?.Left + gap ?? trayLeft - (int)(FallbackTileDips * scale);
                    band = new PixelBox(Math.Min(left, trayLeft - gap), bar.Top, trayLeft, bar.Bottom);
                }

                break;
        }

        return new OverlayTarget(OverlayTargetKind.Docked, taskbar.MonitorId, scale, band, alignRight, height, coversTray);
    }

    private static OverlayTarget Floating(TaskbarInfo taskbar, bool alignRight)
    {
        return new OverlayTarget(OverlayTargetKind.Floating, taskbar.MonitorId, taskbar.Scale, taskbar.WorkArea, alignRight, 0, false);
    }

    /// <summary>The strip of the work area beside a side taskbar, where the vertical tile rides.</summary>
    private static PixelBox SideBand(TaskbarInfo taskbar)
    {
        return taskbar.WorkArea;
    }

    /// <summary>Margins for a floating tile (physical pixels): clear of the scrollbars at the side, a little above the bottom edge.</summary>
    public static (int Side, int Bottom) FloatingMargins(double scale)
    {
        return ((int)(24 * scale), (int)(16 * scale));
    }

    /// <summary>The vertical gap kept between a side taskbar and the vertical tile (physical pixels).</summary>
    public static int SideGap(double scale)
    {
        return (int)(SideGapDips * scale);
    }
}
