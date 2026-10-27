namespace AccountabiliBuddy.Core.Ports;

/// <summary>For cards that must keep the keyboard focus: tells whether another program has taken the foreground, and takes it back.</summary>
public interface IForegroundFocus
{
    /// <returns>True when the foreground window belongs to another program (or there is none, e.g. the Alt+Tab switcher, the Start menu or the lock screen).</returns>
    bool IsForegroundForeign();

    /// <summary>Brings the window to the foreground with the keyboard focus, even though another program has it.</summary>
    /// <returns>False when Windows refused (e.g. the session is locked); the caller should wait a while before trying again.</returns>
    bool TakeForeground(nint windowHandle);
}
