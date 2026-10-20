using Avalonia.Controls;
using Avalonia.Input;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// A text box that records a keyboard shortcut: click it and press the keys (e.g. Ctrl+Alt+F).
/// Typing text is not possible; Backspace clears it.
/// </summary>
public sealed class HotkeyBox : TextBox
{
    protected override Type StyleKeyOverride => typeof(TextBox);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = true;

        if (e.Key is Key.Back or Key.Delete)
        {
            Text = string.Empty;
            return;
        }

        string? key = DescribeKey(e.Key);
        if (key is null)
        {
            return; // A modifier on its own, or an unsupported key: wait for the rest.
        }

        var parts = new List<string>();
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            parts.Add("Win");
        }

        parts.Add(key);
        Text = string.Join("+", parts);
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        e.Handled = true; // Only recorded shortcuts, never typed characters.
    }

    private static string? DescribeKey(Key key)
    {
        return key switch
        {
            >= Key.A and <= Key.Z => key.ToString(),
            >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
            >= Key.F1 and <= Key.F24 => key.ToString(),
            Key.Space => "Space",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Left => "Left",
            Key.Right => "Right",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            _ => null,
        };
    }
}
