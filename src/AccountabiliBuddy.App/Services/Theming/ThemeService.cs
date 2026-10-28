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
        if (_current is null)
        {
            // The one App.axaml merges at startup (Instrument): find it by its source.
            _current = merged.OfType<ResourceInclude>().FirstOrDefault(include => Sources.ContainsValue(include.Source!));
        }

        var next = new ResourceInclude(Sources[theme]) { Source = Sources[theme] };
        if (_current is not null)
        {
            merged.Remove(_current);
        }

        merged.Add(next);
        _current = next;
        Current = theme;
    }
}
