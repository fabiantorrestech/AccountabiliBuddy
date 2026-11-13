using AccountabiliBuddy.App.ViewModels;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>Where the conflicts list gets the tasks "Changed on both devices" and their resolve dialog (the PC's own <see cref="ConflictDialogViewModel"/>).</summary>
public interface ISyncConflictSource
{
    /// <summary>Raised when the list may have changed (the task list changed).</summary>
    event EventHandler? Changed;

    /// <summary>Every live task with a conflict, newest edit first; empty while the task list isn't the source.</summary>
    IReadOnlyList<SyncConflictItem> List();

    /// <summary>The resolve dialog (newer first, keep this one, keep the other, keep both); null when the conflict is gone.</summary>
    ConflictDialogViewModel? DialogFor(Guid id);
}
