namespace AccountabiliBuddy.Core.Tasks;

/// <summary>Where an Archive row came from (AR1-fix): decides what it offers, and what it says after "on".</summary>
public enum ArchiveSource
{
    /// <summary>This PC's own task history (sessions it ran, and "done outside a session" records).</summary>
    Own,

    /// <summary>A past task of another PC kept on this device as a local item (ADR 72).</summary>
    Kept,

    /// <summary>Another PC's task history, as its last sealed sync file carried it (ADR 72).</summary>
    Received,

    /// <summary>The one task list (ADR 76): a task finished there (Queued, Next, To sort, the phone's ticks).</summary>
    TaskList,
}
