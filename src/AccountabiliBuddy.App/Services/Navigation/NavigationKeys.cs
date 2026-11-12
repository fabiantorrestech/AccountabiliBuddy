using Avalonia.Input;

namespace AccountabiliBuddy.App.Services.Navigation;

/// <summary>
/// Which inputs mean back / forward on the PC (owner 2026-11-14): the mouse's back and forward buttons (XButton1 / XButton2) anywhere
/// in the main window, and Alt+← / Alt+→ everywhere except in a text box (the add box moves its split with them, a field moves its caret
/// by word); a key something already handled is left alone. Ctrl+Alt+← / Ctrl+Alt+→ (not these) move a Tasks column.
/// </summary>
public static class NavigationKeys
{
    /// <summary>The way Alt+← / Alt+→ goes, or null when the key is not one of them or stays with what has the keyboard.</summary>
    public static HistoryDirection? FromKey(Key key, KeyModifiers modifiers, bool handled, NavigationFocus focus)
    {
        if (handled || modifiers != KeyModifiers.Alt || focus != NavigationFocus.Other)
        {
            return null;
        }

        return key switch
        {
            Key.Left => HistoryDirection.Back,
            Key.Right => HistoryDirection.Forward,
            _ => null,
        };
    }

    /// <summary>The way a mouse button press goes: XButton1 = back, XButton2 = forward; null for any other button.</summary>
    public static HistoryDirection? FromPointer(PointerUpdateKind kind) => kind switch
    {
        PointerUpdateKind.XButton1Pressed => HistoryDirection.Back,
        PointerUpdateKind.XButton2Pressed => HistoryDirection.Forward,
        _ => null,
    };
}
