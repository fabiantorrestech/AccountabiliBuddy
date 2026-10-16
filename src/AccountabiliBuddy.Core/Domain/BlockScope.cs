namespace AccountabiliBuddy.Core.Domain;

/// <summary>How much of an app a matching block rule covers. Derived from the rule's target.</summary>
public enum BlockScope
{
    /// <summary>The whole app (process rule). Enforcement minimizes it.</summary>
    App,

    /// <summary>One view inside an app (title, URL or OCR rule). Enforcement keeps the overlay up instead of minimizing,
    /// so the rest of the app stays usable.</summary>
    View,
}
