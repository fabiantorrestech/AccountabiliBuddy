namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>A change of the task store (ADR 76): who made it, so the view that made it doesn't take it in twice.</summary>
/// <param name="Source">The PC's old list that made the change; null for a sync file, a backup or the store itself.</param>
/// <param name="FromOtherDevice">The change came in from another device's file (or a backup), not from this PC's own screens.</param>
public sealed class TaskStoreChangedEventArgs(object? Source, bool FromOtherDevice) : EventArgs
{
    public object? Source { get; } = Source;

    public bool FromOtherDevice { get; } = FromOtherDevice;
}
