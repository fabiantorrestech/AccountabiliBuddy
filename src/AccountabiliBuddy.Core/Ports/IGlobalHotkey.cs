using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Ports;

/// <summary>A system-wide keyboard shortcut that works while the app is in the background.</summary>
public interface IGlobalHotkey
{
    /// <summary>Raised when the shortcut is pressed. May be raised on a background thread.</summary>
    event EventHandler? Pressed;

    /// <summary>Registers <paramref name="chord"/>, replacing any shortcut registered before.</summary>
    /// <param name="error">Why it failed, e.g. another app already uses the shortcut.</param>
    bool TryRegister(HotkeyChord chord, out string? error);

    void Unregister();
}
