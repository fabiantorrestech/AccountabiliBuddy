namespace AccountabiliBuddy.Core.Domain;

/// <summary>What the app should do on one monitor right now.</summary>
public enum EnforcementAction
{
    None,

    /// <summary>Toast in the corner of the monitor.</summary>
    Nudge,

    /// <summary>Dimmed, click-blocking layer over the monitor.</summary>
    Overlay,

    /// <summary>Minimize the offending window (whole-app blocks only).</summary>
    Minimize,

    /// <summary>Send the browser tab to the extension's block page (URL and channel blocks with a known tab).</summary>
    RedirectTab,
}
