namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>A change of the goals store: who made it, so the screen that made it doesn't take it in twice.</summary>
/// <param name="Source">The screen or service that made the change; null for the store itself, a backup or the Deep work rules.</param>
/// <param name="FromBackup">The change came from a restored backup, not from this PC's own screens.</param>
public sealed class GoalStoreChangedEventArgs(object? Source, bool FromBackup) : EventArgs
{
    public object? Source { get; } = Source;

    public bool FromBackup { get; } = FromBackup;
}
