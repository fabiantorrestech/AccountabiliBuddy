using System.Diagnostics.CodeAnalysis;

namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// A global keyboard shortcut such as "Ctrl+Alt+F". At least one modifier is required so a plain key press
/// is never swallowed system-wide. Keys: A–Z, 0–9, F1–F24, Space, the arrow keys and Page Up/Down.
/// </summary>
public sealed record HotkeyChord(HotkeyModifiers Modifiers, string Key)
{
    public static HotkeyChord Default { get; } = new(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, "F");

    public static bool TryParse(string? text, [NotNullWhen(true)] out HotkeyChord? chord, [NotNullWhen(false)] out string? error)
    {
        chord = null;
        string[] parts = (text ?? string.Empty).Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            error = "Use at least one modifier plus a key, e.g. Ctrl+Alt+F.";
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        foreach (string part in parts[..^1])
        {
            HotkeyModifiers? modifier = part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => HotkeyModifiers.Ctrl,
                "alt" => HotkeyModifiers.Alt,
                "shift" => HotkeyModifiers.Shift,
                "win" or "windows" => HotkeyModifiers.Win,
                _ => null,
            };

            if (modifier is null)
            {
                error = $"'{part}' is not a modifier (use Ctrl, Alt, Shift or Win).";
                return false;
            }

            modifiers |= modifier.Value;
        }

        string key = NormalizeKey(parts[^1]);
        if (!IsSupportedKey(key))
        {
            error = $"'{parts[^1]}' is not a supported key (A–Z, 0–9, F1–F24, Space, arrows or Page Up/Down).";
            return false;
        }

        if (modifiers == HotkeyModifiers.Shift)
        {
            error = "Shift alone is not enough; add Ctrl, Alt or Win.";
            return false;
        }

        chord = new HotkeyChord(modifiers, key);
        error = null;
        return true;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        foreach (HotkeyModifiers modifier in new[] { HotkeyModifiers.Ctrl, HotkeyModifiers.Alt, HotkeyModifiers.Shift, HotkeyModifiers.Win })
        {
            if (Modifiers.HasFlag(modifier))
            {
                parts.Add(modifier.ToString());
            }
        }

        parts.Add(Key);
        return string.Join("+", parts);
    }

    private static readonly string[] NamedKeys = { "Space", "Up", "Down", "Left", "Right", "PageUp", "PageDown" };

    private static string NormalizeKey(string key)
    {
        return NamedKeys.FirstOrDefault(named => named.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? key.ToUpperInvariant();
    }

    private static bool IsSupportedKey(string key)
    {
        if (NamedKeys.Contains(key))
        {
            return true;
        }

        if (key.Length == 1)
        {
            return char.IsAsciiLetterUpper(key[0]) || char.IsAsciiDigit(key[0]);
        }

        return key.StartsWith('F') && int.TryParse(key[1..], out int number) && number is >= 1 and <= 24;
    }
}
