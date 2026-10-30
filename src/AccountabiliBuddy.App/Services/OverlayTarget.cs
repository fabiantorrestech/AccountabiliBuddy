using AccountabiliBuddy.Core.Ports;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Where and in what form the overlay should be right now: a band on a monitor (physical pixels) the tile is laid in, and which side of
/// the band it hugs. The tile's own width is only known after it is laid out, so the window finishes the job.
/// </summary>
/// <param name="Kind">What to draw.</param>
/// <param name="MonitorId">The monitor's device name; null when no taskbar is known (the window then uses the main monitor).</param>
/// <param name="Scale">The monitor's display scaling.</param>
/// <param name="Band">The room the tile may use: for a docked tile the taskbar's free stretch, for a floating one the work area's lower corner.</param>
/// <param name="AlignRight">The tile hugs the band's right edge (otherwise its left edge).</param>
/// <param name="TaskbarHeight">The taskbar's thickness in physical pixels (the docked tile's height); 0 for floating.</param>
/// <param name="CoversTray">The tile covers the tray, so it brings its own clock, batteries and ⌃ button.</param>
/// <param name="Edge">The taskbar edge, for the vertical tile.</param>
public sealed record OverlayTarget(
    OverlayTargetKind Kind,
    string? MonitorId,
    double Scale,
    PixelBox Band,
    bool AlignRight,
    int TaskbarHeight,
    bool CoversTray,
    TaskbarEdge Edge = TaskbarEdge.Bottom)
{
    public static OverlayTarget Hidden { get; } = new(OverlayTargetKind.Hidden, null, 1, new PixelBox(0, 0, 0, 0), true, 0, false);
}
