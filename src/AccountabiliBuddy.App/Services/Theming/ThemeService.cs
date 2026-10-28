using Avalonia;
using Avalonia.Markup.Xaml.Styling;

namespace AccountabiliBuddy.App.Services.Theming;

/// <summary>
/// Swaps the active theme's token dictionary (Themes/*.axaml, ADR 49) in the application resources. Light or dark stays
/// with <see cref="AppTheme"/>. Use from the UI thread.
/// </summary>
public sealed class ThemeService
{
    private static readonly Dictionary<ThemeName, Uri> Sources = new()
    {
        [ThemeName.Instrument] = new Uri("avares://AccountabiliBuddy.App/Themes/Instrument.axaml"),
        [ThemeName.NightDesk] = new Uri("avares://AccountabiliBuddy.App/Themes/NightDesk.axaml"),
        [ThemeName.Paper] = new Uri("avares://AccountabiliBuddy.App/Themes/Paper.axaml"),
    };

    private ResourceInclude? _current;

    public ThemeName Current { get; private set; } = ThemeName.Instrument;

    /// <summary>Replaces the current theme dictionary with <paramref name="theme"/>'s.</summary>
    public void Apply(ThemeName theme)
    {
        if (Application.Current is not Application app)
        {
            return;
        }

        var merged = app.Resources.MergedDictionaries;
        var next = new ResourceInclude(Sources[theme]) { Source = Sources[theme] };

        // Add the new one first, then drop the old: one pass, and Ab.* is never unset in between.
        merged.Add(next);
        if (_current is not null)
        {
            merged.Remove(_current);
        }

        _current = next;
        Current = theme;
    }
}
