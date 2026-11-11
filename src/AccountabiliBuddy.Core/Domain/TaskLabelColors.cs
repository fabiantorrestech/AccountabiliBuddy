using System.Globalization;

namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// Label colours (ADR 76): the 12-colour set new labels start on, and the forms a colour may take: "#RRGGBB", "#RRGGBBAA" or
/// "rgba(r,g,b,a)" with a from 0.2 to 1. The old category colours (<see cref="TaskCategory.Palette"/>) stay valid. The old sync
/// fields only take "#RRGGBB", so they get <see cref="Opaque"/>.
/// </summary>
public static class TaskLabelColors
{
    /// <summary>The 12 colours a new label starts on, the first unused one first.</summary>
    public static IReadOnlyList<string> Set { get; } =
    [
        "#2F6BEA", "#14998C", "#EA6A12", "#E0322C", "#7C4DFF", "#22A350",
        "#C2410C", "#0E7490", "#A21CAF", "#4D7C0F", "#B45309", "#BE123C",
    ];

    /// <summary>The lowest opacity a custom colour may have.</summary>
    public const double MinOpacity = 0.2;

    /// <summary>Whether the text is a colour in one of the accepted forms.</summary>
    public static bool IsValid(string? color)
    {
        return Parse(color) is not null;
    }

    /// <summary>The colour without its opacity, "#RRGGBB" in upper case; the set's first colour when it can't be read.</summary>
    public static string Opaque(string? color)
    {
        (byte Red, byte Green, byte Blue, double Alpha)? parsed = Parse(color);
        if (parsed is null)
        {
            return Set[0];
        }

        return string.Create(CultureInfo.InvariantCulture, $"#{parsed.Value.Red:X2}{parsed.Value.Green:X2}{parsed.Value.Blue:X2}");
    }

    /// <summary>Whether two colours look the same once their opacity is ignored.</summary>
    public static bool SameOpaque(string? first, string? second)
    {
        return string.Equals(Opaque(first), Opaque(second), StringComparison.Ordinal);
    }

    /// <summary>The first colour of <see cref="Set"/> no live label uses (by its opaque form); the first one when all are used.</summary>
    public static string FirstUnused(IEnumerable<TaskLabel> labels)
    {
        HashSet<string> used = labels.Where(label => label.IsLive).Select(label => Opaque(label.Color)).ToHashSet(StringComparer.Ordinal);
        return Set.FirstOrDefault(color => !used.Contains(color)) ?? Set[0];
    }

    private static (byte Red, byte Green, byte Blue, double Alpha)? Parse(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return null;
        }

        string text = color.Trim();
        if (text.StartsWith('#') && (text.Length == 7 || text.Length == 9) && text[1..].All(char.IsAsciiHexDigit))
        {
            byte red = byte.Parse(text.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte green = byte.Parse(text.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte blue = byte.Parse(text.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            double alpha = text.Length == 9 ? byte.Parse(text.AsSpan(7, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0 : 1.0;
            return (red, green, blue, alpha);
        }

        if (text.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase) && text.EndsWith(')'))
        {
            string[] parts = text[5..^1].Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length == 4
                && byte.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte red)
                && byte.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte green)
                && byte.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte blue)
                && double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double alpha)
                && alpha is >= 0 and <= 1)
            {
                return (red, green, blue, alpha);
            }
        }

        return null;
    }
}
