namespace AccountabiliBuddy.App.Services.Theming;

/// <summary>Where the place for After sunset comes from (ADR 49).</summary>
public enum LocationSource
{
    /// <summary>Windows location, read at coarse accuracy at most once a day.</summary>
    Windows = 0,

    /// <summary>A latitude and longitude typed in.</summary>
    Typed = 1,

    /// <summary>A city picked from the bundled list.</summary>
    City = 2,
}
