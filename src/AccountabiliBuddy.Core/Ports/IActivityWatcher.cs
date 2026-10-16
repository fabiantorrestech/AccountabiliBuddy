using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Ports;

/// <summary>Reports what is visible on each monitor. Implemented per platform.</summary>
public interface IActivityWatcher
{
    /// <summary>Raised when the top visible window, or its title, changes on any monitor.</summary>
    event EventHandler<ActivitySnapshot>? ActivityChanged;

    /// <summary>Raised with the monitor id when a monitor is no longer connected.</summary>
    event EventHandler<string>? MonitorDisconnected;

    void Start();

    void Stop();

    /// <summary>One snapshot per connected monitor, for periodic rechecks.</summary>
    IReadOnlyList<ActivitySnapshot> CaptureCurrent();
}
