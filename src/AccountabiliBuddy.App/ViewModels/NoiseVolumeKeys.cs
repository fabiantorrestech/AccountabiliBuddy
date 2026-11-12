using Avalonia.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The title bar's noise volume popover keys (round 2, brief §4.2): arrows ±5, Shift+arrows ±1, Page Up/Down ±10, Home 0,
/// End 100; a digit starts typing a value. Volumes are whole numbers 0–100.
/// </summary>
public static class NoiseVolumeKeys
{
    public const int Minimum = 0;
    public const int Maximum = 100;
    public const int Step = 5;
    public const int FineStep = 1;
    public const int PageStep = 10;

    /// <summary>The volume after <paramref name="key"/>, or null when the key isn't one of the volume keys.</summary>
    public static int? Apply(int volume, Key key, KeyModifiers modifiers)
    {
        int step = modifiers.HasFlag(KeyModifiers.Shift) ? FineStep : Step;
        int? next = key switch
        {
            Key.Right or Key.Up => volume + step,
            Key.Left or Key.Down => volume - step,
            Key.PageUp => volume + PageStep,
            Key.PageDown => volume - PageStep,
            Key.Home => Minimum,
            Key.End => Maximum,
            _ => null,
        };
        return next is int value ? Clamp(value) : null;
    }

    /// <summary>The digit a key types (top row or number pad), or null.</summary>
    public static int? DigitOf(Key key)
    {
        if (key >= Key.D0 && key <= Key.D9)
        {
            return key - Key.D0;
        }

        if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            return key - Key.NumPad0;
        }

        return null;
    }

    /// <summary>A typed volume, clamped to 0–100; null when nothing usable was typed (keep the old value).</summary>
    public static int? ParseTyped(string? text)
    {
        string digits = new((text ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 0)
        {
            return null;
        }

        // At most three digits are typed; a longer paste is still clamped rather than overflowing.
        return int.TryParse(digits.Length > 3 ? digits[..3] : digits, out int value) ? Clamp(value) : null;
    }

    public static int Clamp(int volume) => Math.Clamp(volume, Minimum, Maximum);
}
