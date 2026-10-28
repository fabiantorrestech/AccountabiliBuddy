namespace AccountabiliBuddy.Core.Ports;

/// <summary>
/// Where the Windows taskbars are and whether the app in front is full screen, so the session overlay can dock over a taskbar
/// (ADR 51). Event-driven: it recalculates on appbar notifications, display, DPI and taskbar setting changes and when the window
/// in front changes, never on a timer. Nothing here changes the user's taskbar or display settings.
/// </summary>
public interface ITaskbarGeometry
{
    /// <summary>The taskbars known now, the main one first. Empty when the shell has none (Explorer is restarting).</summary>
    IReadOnlyList<TaskbarInfo> Taskbars { get; }

    /// <summary>The window in front: full screen or not, and a game or not.</summary>
    ForegroundScreenState Foreground { get; }

    /// <summary>Raised after any of the above changed. May be raised on a background thread.</summary>
    event EventHandler? Changed;

    /// <summary>Reads everything again now; <see cref="Changed"/> follows when something differs.</summary>
    void Refresh();
}
