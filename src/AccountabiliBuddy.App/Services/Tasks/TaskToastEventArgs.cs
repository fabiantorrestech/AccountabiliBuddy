namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>A toast the task actions ask the screen to show (spec 05 section 14): its text (holds task titles: shown, never logged) and whether it offers Undo.</summary>
public sealed class TaskToastEventArgs : EventArgs
{
    public TaskToastEventArgs(string text, bool canUndo)
    {
        Text = text;
        CanUndo = canUndo;
    }

    public string Text { get; }

    /// <summary>The Undo button shows (6 seconds instead of 4); Ctrl+Z works either way while the stack holds the action.</summary>
    public bool CanUndo { get; }
}
