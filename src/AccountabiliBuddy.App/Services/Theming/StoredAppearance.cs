namespace AccountabiliBuddy.App.Services.Theming;

/// <summary>
/// The "theme" document. Grown from <c>{"Choice":n}</c>: a document with only Choice still loads, with every newer field at
/// its default (Theme null means the user never picked one). The place for After sunset is private: only
/// <see cref="ProtectedLocation"/> holds it (DPAPI, base64), including a picked city's name; the date it was last asked for
/// is not private.
/// </summary>
internal sealed class StoredAppearance
{
    /// <summary>A <see cref="LightDarkMode"/>.</summary>
    public int Choice { get; set; }

    /// <summary>A <see cref="ThemeName"/> by name; null until the user picks one.</summary>
    public string? Theme { get; set; }

    public TimeOnly DarkAt { get; set; } = new(19, 0);

    public TimeOnly LightAt { get; set; } = new(7, 0);

    /// <summary>A <see cref="LocationSource"/>.</summary>
    public int LocationSource { get; set; }

    /// <summary>The place as an encrypted <see cref="StoredPlace"/>; null when none is stored.</summary>
    public string? ProtectedLocation { get; set; }

    /// <summary>The day Windows location was last asked for (the once-a-day limit), whether or not it answered.</summary>
    public DateOnly? LocationAskedOn { get; set; }

    /// <summary>A <see cref="ColourMode"/>.</summary>
    public int Colour { get; set; }
}
