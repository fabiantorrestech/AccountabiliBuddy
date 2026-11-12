namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// A toast the task actions ask the screen to show (spec 05 section 14): its text (holds task titles: shown, never logged), whether it
/// offers Undo, and whether it explains something (what a swap or an undo did, why something wasn't done), which keeps it longer.
/// </summary>
public sealed class TaskToastEventArgs : EventArgs
{
    public TaskToastEventArgs(string text, bool canUndo, bool explains = false)
    {
        Text = text;
        CanUndo = canUndo;
        Explains = explains;
    }

    public string Text { get; }

    /// <summary>The Undo button shows (6 seconds instead of 4); Ctrl+Z works either way while the stack holds the action.</summary>
    public bool CanUndo { get; }

    /// <summary>An explanation: 7 seconds.</summary>
    public bool Explains { get; }
}
