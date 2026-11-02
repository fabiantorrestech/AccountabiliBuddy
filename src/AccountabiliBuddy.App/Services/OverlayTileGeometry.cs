using AccountabiliBuddy.Core.Ports;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// How tall the overlay tile is and where its top-left corner goes once its width is known (ADR 51). The overlay window and the live
/// preview in Settings both ask here, so the two cannot drift apart. Pure: no windows, nothing read from the machine.
/// </summary>
public static class OverlayTileGeometry
{
    // Heights in device-independent pixels where the taskbar doesn't give one (floating and side tiles).
    public const double SmallHeight = 36;
    public const double FloatingHeight = 56;
    public const double LargeRise = 36;
    public const double SideTileHeight = 110;

    /// <summary>The tile's height in device-independent pixels.</summary>
    public static double HeightFor(OverlayTarget target, OverlaySettings settings)
    {
        double bar = target.TaskbarHeight > 0 ? target.TaskbarHeight / target.Scale : FloatingHeight;
        return target.Kind == OverlayTargetKind.VerticalTile ? SideTileHeight
            : settings.Size switch
            {
                OverlaySize.Small => SmallHeight,
                OverlaySize.Large => bar + LargeRise,
                _ => bar,
            };
    }

    /// <summary>
    /// The tile's top-left corner in physical pixels.
    /// </summary>
    /// <param name="widthPixels">The tile's laid-out width, physical pixels.</param>
    /// <param name="heightPixels">The tile's height, physical pixels.</param>
    /// <param name="scale">The tile window's display scaling (1 in the preview).</param>
    /// <param name="rule">The running app's own rule, if it has one: its offsets apply to floating tiles and the Large size.</param>
    /// <param name="fallbackArea">The work area to float in when no taskbar (so no monitor) is known.</param>
    public static (int X, int Y) PositionFor(
        OverlayTarget target, OverlaySettings settings, OverlayAppRule? rule, int widthPixels, int heightPixels, double scale, PixelBox fallbackArea)
    {
        PixelBox band = target.Band;
        int x;
        int y;
        switch (target.Kind)
        {
            case OverlayTargetKind.Docked:
                x = target.AlignRight ? band.Right - widthPixels : band.Left;
                y = settings.Size == OverlaySize.Small ? band.Top + ((target.TaskbarHeight - heightPixels) / 2)
                    : settings.Size == OverlaySize.Large ? band.Top - (int)(LargeRise * scale)
                    : band.Top;
                break;
            case OverlayTargetKind.VerticalTile:
                int gap = OverlayPlacementCalculator.SideGap(scale);
                x = target.Edge == TaskbarEdge.Right ? band.Right - widthPixels - gap : band.Left + gap;
                y = settings.SideTileY ?? band.Top + ((band.Height - heightPixels) / 2);
                y = Math.Clamp(y, band.Top, Math.Max(band.Top, band.Bottom - heightPixels));
                break;
            default:
                (int side, int bottom) = OverlayPlacementCalculator.FloatingMargins(scale);
                PixelBox area = target.MonitorId is null ? fallbackArea : band;
                x = target.AlignRight ? area.Right - widthPixels - side : area.Left + side;
                y = area.Bottom - heightPixels - bottom;
                break;
        }

        if (rule is not null && (target.Kind == OverlayTargetKind.Floating || settings.Size == OverlaySize.Large))
        {
            x += (int)(rule.OffsetX * scale);
            y += RuleShiftY(target, (int)(rule.OffsetY * scale));
        }

        return (x, y);
    }

    // A floating tile already sits LiftPixels higher than usual while a shown auto-hide taskbar is under it. The rule's own vertical
    // offset (negative = up) and that lift do not add up: the higher of the two wins, so the tile moves up only by what the rule asks
    // beyond the lift. Everywhere else the rule's offset applies in full.
    private static int RuleShiftY(OverlayTarget target, int offsetPixels)
    {
        return target.Kind == OverlayTargetKind.Floating && offsetPixels <= 0 ? Math.Min(0, offsetPixels + target.LiftPixels) : offsetPixels;
    }
}
