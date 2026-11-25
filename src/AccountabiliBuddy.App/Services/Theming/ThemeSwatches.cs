using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;

namespace AccountabiliBuddy.App.Services.Theming;

/// <summary>
/// A theme's swatch colours (Tasks redesign 9.23): its Bg, Surface2 and Accent in light or dark, read from the theme's own token file
/// (Themes/*.axaml, the same files <see cref="ThemeService"/> swaps in), so a swatch never drifts from the theme. Each file is read once.
/// Use from the UI thread.
/// </summary>
public static class ThemeSwatches
{
    // The same files as ThemeService's (it keeps its map private).
    private static readonly Dictionary<ThemeName, Uri> Sources = new()
    {
        [ThemeName.Instrument] = new Uri("avares://AccountabiliBuddy.App/Themes/Instrument.axaml"),
        [ThemeName.NightDesk] = new Uri("avares://AccountabiliBuddy.App/Themes/NightDesk.axaml"),
        [ThemeName.Paper] = new Uri("avares://AccountabiliBuddy.App/Themes/Paper.axaml"),
    };

    private static readonly Dictionary<ThemeName, IResourceProvider?> Loaded = [];

    /// <summary>The three stripes, left to right: Bg, Surface2, Accent (grey when the file can't be read).</summary>
    public static (Color Bg, Color Surface2, Color Accent) Of(ThemeName theme, bool dark)
    {
        IResourceProvider? tokens = TokensOf(theme);
        ThemeVariant variant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        return (ColorOf(tokens, "Ab.BgColor", variant), ColorOf(tokens, "Ab.Surface2Color", variant), ColorOf(tokens, "Ab.AccentColor", variant));
    }

    private static IResourceProvider? TokensOf(ThemeName theme)
    {
        if (!Loaded.TryGetValue(theme, out IResourceProvider? tokens))
        {
            try
            {
                tokens = new ResourceInclude(Sources[theme]) { Source = Sources[theme] }.Loaded;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or Avalonia.Markup.Xaml.XamlLoadException)
            {
                tokens = null;
            }

            Loaded[theme] = tokens;
        }

        return tokens;
    }

    private static Color ColorOf(IResourceProvider? tokens, string key, ThemeVariant variant)
    {
        return tokens is not null && tokens.TryGetResource(key, variant, out object? value) && value is Color color ? color : Colors.Gray;
    }
}
