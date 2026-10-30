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

        if (taskbar.AutoHide)
        {
            if (taskbar.IsShown)
            {
                // The taskbar is up: docking over it would put the tile behind the sliding bar, so the tile floats as it did while the bar was
                // hidden, lifted clear above the bar's top edge (all three auto-hide modes show the tile now: the slim line and "hide with it"
                // end when the taskbar comes back). Floating() lifts only when the bar would cover the tile; the tile drops back when it hides.
                return Floating(taskbar, alignRight: true);
            }

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
        return new OverlayTarget(OverlayTargetKind.Floating, taskbar.MonitorId, taskbar.Scale, LiftedAboveHidingTaskbar(taskbar), alignRight, 0, false);
    }

    /// <summary>
    /// The work area of a floating tile. An auto-hiding taskbar reserves nothing, so the tile sits near the screen's bottom edge, where the
    /// bar slides over it; while the bar is (partly) up, the area's bottom is raised so the tile's bottom lands a gap above the bar's top
    /// edge. A tile already higher than that stays where it is (the area is only ever shortened), and with the bar hidden (its top is at
    /// the screen's bottom edge) nothing changes.
    /// </summary>
    private static PixelBox LiftedAboveHidingTaskbar(TaskbarInfo taskbar)
    {
        PixelBox area = taskbar.WorkArea;
        if (!taskbar.AutoHide || taskbar.Edge != TaskbarEdge.Bottom)
        {
            return area;
        }

        (_, int bottomMargin) = FloatingMargins(taskbar.Scale);
        int lifted = taskbar.Bounds.Top - (int)(GapDips * taskbar.Scale) + bottomMargin;
        return lifted < area.Bottom ? area with { Bottom = lifted } : area;
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
