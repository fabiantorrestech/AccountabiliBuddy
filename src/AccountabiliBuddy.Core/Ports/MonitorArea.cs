namespace AccountabiliBuddy.Core.Ports;

/// <summary>A monitor's position in physical pixels on the virtual desktop.</summary>
/// <param name="Bounds">The whole monitor.</param>
/// <param name="WorkArea">The monitor minus the taskbar and docked toolbars.</param>
public sealed record MonitorArea(PixelBox Bounds, PixelBox WorkArea);
