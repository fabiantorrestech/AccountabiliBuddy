namespace AccountabiliBuddy.Core.Domain;

/// <summary>Response to drift, in increasing strength.</summary>
public enum EscalationLevel
{
    None,
    /// <summary>Toast notification.</summary>
    Nudge,
    /// <summary>Overlay on the offending monitor.</summary>
    Overlay,
    /// <summary>Minimize the window or redirect the browser tab.</summary>
    Enforce,
}
