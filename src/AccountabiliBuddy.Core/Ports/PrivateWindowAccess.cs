namespace AccountabiliBuddy.Core.Ports;

/// <summary>Whether the AccountabiliBuddy extension may run in a browser's private (incognito) windows.</summary>
public enum PrivateWindowAccess
{
    /// <summary>The browser's settings couldn't be read.</summary>
    Unknown,

    /// <summary>The extension isn't installed in any profile of the browser.</summary>
    NotInstalled,

    /// <summary>Allowed in every profile that has the extension.</summary>
    Allowed,

    /// <summary>At least one profile has the extension without private-window access.</summary>
    NotAllowed,
}
