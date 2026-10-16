namespace AccountabiliBuddy.Core.Ports;

/// <summary>Finds where a monitor is on the virtual desktop, so toasts and overlays can be placed on it.</summary>
public interface IMonitorLocator
{
    /// <returns>The monitor's area, or null if it is no longer connected.</returns>
    MonitorArea? Find(string monitorId);
}
