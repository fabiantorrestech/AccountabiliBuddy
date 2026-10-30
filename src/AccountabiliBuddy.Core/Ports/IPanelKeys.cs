using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Ports;

/// <summary>A set of system-wide shortcuts held only while the overlay panel is open (Ctrl+Alt+D for Done and so on), so keys work while another app has focus.</summary>
public interface IPanelKeys
{
    /// <summary>Raised when one of the registered shortcuts is pressed. Raised on a background thread.</summary>
    event EventHandler<HotkeyChord>? Pressed;

    /// <summary>Registers these shortcuts, replacing any registered before. Shortcuts another app holds are skipped; they are returned.</summary>
    IReadOnlyList<HotkeyChord> Register(IReadOnlyList<HotkeyChord> chords);

    void Unregister();
}
