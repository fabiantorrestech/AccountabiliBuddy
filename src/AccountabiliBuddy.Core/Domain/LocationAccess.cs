namespace AccountabiliBuddy.Core.Domain;

/// <summary>Whether the app may read this computer's location (ADR 49).</summary>
public enum LocationAccess
{
    /// <summary>Not asked yet.</summary>
    Unknown,
    Allowed,

    /// <summary>Location is off for this app, or for the whole computer.</summary>
    Denied,

    /// <summary>Allowed, but no position could be worked out (no sensor, or it took too long).</summary>
    Unavailable,
}
