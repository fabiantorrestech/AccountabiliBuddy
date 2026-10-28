namespace AccountabiliBuddy.App.Services.Theming;

/// <summary>How the app picks light or dark. The numbers are stored (document "theme", field Choice); the first three are the old ones.</summary>
public enum LightDarkMode
{
    /// <summary>Follows Windows' own setting.</summary>
    System = 0,
    Light = 1,
    Dark = 2,

    /// <summary>Dark from one time of day to another.</summary>
    SetTime = 3,

    /// <summary>Dark from sunset to sunrise at a known place.</summary>
    Sunset = 4,
}
