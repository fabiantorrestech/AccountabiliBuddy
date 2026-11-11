namespace AccountabiliBuddy.Core.Tasks;

/// <summary>What to do with a task's calendar event (ADR 78).</summary>
public enum TaskCalendarActionKind
{
    Delete,
    Update,
    Create,
}
