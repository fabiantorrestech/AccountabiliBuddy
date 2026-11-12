namespace AccountabiliBuddy.App.Services.Navigation;

/// <summary>
/// A back / forward move is about to show its place. The window can play the page slide around it: it calls <see cref="Apply"/>
/// (once) when the old page is out, and sets <see cref="Handled"/>; when nobody handles it the view model applies it at once.
/// </summary>
public sealed class HistoryNavigatingEventArgs : EventArgs
{
    public HistoryNavigatingEventArgs(HistoryDirection direction, bool sameTab, Action apply)
    {
        Direction = direction;
        SameTab = sameTab;
        Apply = apply;
    }

    public HistoryDirection Direction { get; }

    /// <summary>True when the place is on the tab already showing (an opened task, a picked goal, a Settings section).</summary>
    public bool SameTab { get; }

    /// <summary>Puts the place back (selects the tab, the Settings row, the task or the goal).</summary>
    public Action Apply { get; }

    /// <summary>Set by the handler that takes over calling <see cref="Apply"/>.</summary>
    public bool Handled { get; set; }
}
