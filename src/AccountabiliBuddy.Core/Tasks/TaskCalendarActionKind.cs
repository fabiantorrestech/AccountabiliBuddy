namespace AccountabiliBuddy.Core.Tasks;

/// <summary>What to do with a task's calendar event (ADR 78).</summary>
public enum TaskCalendarActionKind
{
    /// <summary>
    /// Drop this PC's record and leave the event alone: the task still wants it but is no longer kept on this device, and the
    /// devices that keep it share the same event id.
    /// </summary>
    Forget,

    Delete,
    Update,
    Create,
}
