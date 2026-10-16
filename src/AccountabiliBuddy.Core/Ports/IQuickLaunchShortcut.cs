namespace AccountabiliBuddy.Core.Ports;

/// <summary>
/// A Start menu shortcut that opens the quick-session popup (and can be pinned to the taskbar by the user).
/// </summary>
public interface IQuickLaunchShortcut
{
    bool IsInstalled { get; }

    void Install();

    void Remove();
}
