namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// "Add new reminders to" (ADR 78): where a task's reminders and due date go by default when it first gets one. Existing tasks are
/// never exported because of it; each task's own "Add to Google Calendar" switch (<see cref="Domain.TaskItem.Calendar"/>) decides.
/// </summary>
public enum TaskCalendarDestination
{
    /// <summary>The app's own "AB Reminders" calendar in the connected Google account (the default).</summary>
    GoogleCalendar,

    /// <summary>Nowhere: new tasks start with the switch off.</summary>
    Nowhere,
}
