namespace AccountabiliBuddy.Core.Ports;

/// <summary>One taskbar (the main one, or the one an extra monitor shows) as the overlay needs to know it. All boxes are physical pixels.</summary>
/// <param name="MonitorId">The monitor's device name ("\.\DISPLAY1"), the id used in activity snapshots.</param>
/// <param name="Monitor">The whole monitor.</param>
/// <param name="Scale">The monitor's display scaling (1.5 = 150%).</param>
/// <param name="Bounds">Where the taskbar is right now. An auto-hiding taskbar that is slid away leaves only a thin strip on screen.</param>
/// <param name="AutoHide">The "Automatically hide the taskbar" setting is on.</param>
/// <param name="IsShown">The taskbar is on screen: always for a fixed one, only while slid in for an auto-hiding one.</param>
/// <param name="FreeArea">The stretch of a horizontal taskbar between the app icons and the tray, where the docked overlay may sit; null when it
/// could not be read (the adapter's safe fallback is then the caller's: next to the tray).</param>
/// <param name="TrayBounds">The notification area (tray, clock); null when it could not be read.</param>
/// <param name="LeftFreeArea">The stretch left of the app icons when they are centered (Windows 11's default); null when the icons start at the left or it could not be read.</param>
/// <param name="IconsLeftAligned">The app icons start at the taskbar's left end (Windows 10, or Windows 11 set to left).</param>
public sealed record TaskbarInfo(
    string MonitorId,
    bool IsPrimary,
    PixelBox Monitor,
    PixelBox WorkArea,
    double Scale,
    TaskbarEdge Edge,
    PixelBox Bounds,
    bool AutoHide,
    bool IsShown,
    PixelBox? FreeArea,
    PixelBox? TrayBounds,
    PixelBox? LeftFreeArea = null,
    bool IconsLeftAligned = true)
{
    /// <summary>The taskbar sits on a side of the screen: the overlay then uses the vertical tile.</summary>
    public bool IsVertical => Edge is TaskbarEdge.Left or TaskbarEdge.Right;
}
