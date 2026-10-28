using Avalonia.Media;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// The five steps of a heatmap, from the theme's own colours: level 0 is the empty colour (Surface2), level 4 the accent, and
/// the steps between mix the two (the design's dark ramp, #1F2532 to #D2B98F, derived the same way for every theme).
/// </summary>
public static class HeatRamp
{
    private static readonly double[] Mix = [0, 0.22, 0.45, 0.72, 1];

    /// <summary>The brush for a level 0 to 4; the empty brush when a brush isn't a plain colour.</summary>
    public static IBrush[] Brushes(IBrush? empty, IBrush? accent)
    {
        if (empty is not ISolidColorBrush from || accent is not ISolidColorBrush to)
        {
            IBrush fallback = empty ?? accent ?? Avalonia.Media.Brushes.Gray;
            return [fallback, fallback, fallback, fallback, fallback];
        }

        return Mix.Select(share => (IBrush)new SolidColorBrush(Lerp(from.Color, to.Color, share))).ToArray();
    }

    private static Color Lerp(Color from, Color to, double share)
    {
        byte Channel(byte a, byte b) => (byte)Math.Round(a + (b - a) * share);
        return Color.FromRgb(Channel(from.R, to.R), Channel(from.G, to.G), Channel(from.B, to.B));
    }
}
