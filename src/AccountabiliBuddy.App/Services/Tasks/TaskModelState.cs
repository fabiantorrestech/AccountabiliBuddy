namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>Where this PC's task lists come from after the startup step (ADR 76, <see cref="TaskModelMigration"/>).</summary>
public enum TaskModelState
{
    /// <summary>No migration yet (it failed or didn't balance): the old lists are the source, as before ADR 76.</summary>
    OldLists,

    /// <summary>The one task list is the source.</summary>
    TaskList,

    /// <summary>
    /// The migration ran but the task list can't be read. The old documents are stale since then, so they are never the source:
    /// the task lists stay empty, unshared, unsaved and out of backups until the user rebuilds them.
    /// </summary>
    Unreadable,
}
