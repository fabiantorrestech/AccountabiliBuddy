namespace AccountabiliBuddy.App;

/// <summary>What a second launch asks the running copy to do.</summary>
public enum InstanceSignal
{
    /// <summary>Bring the main window to the front (plain second launch).</summary>
    Show,

    /// <summary>Open the quick-session popup (Start menu shortcut).</summary>
    QuickSession,
}
