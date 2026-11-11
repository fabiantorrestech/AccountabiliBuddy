using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// The per-task "Add to Google Calendar" default (ADR 78): a task that gets its first reminder or due date takes it from "Add new
/// reminders to". Nothing else sets it, so tasks that already had reminders are never exported without the user turning it on.
/// The Reminders &amp; due date menu (M5) calls it on every edit it makes.
/// </summary>
public static class TaskCalendarOptIn
{
    /// <returns><paramref name="after"/>, with <see cref="TaskItem.Calendar"/> set from <paramref name="destination"/> when this edit gave the task its first reminder or due date.</returns>
    public static TaskItem WithDefault(TaskItem before, TaskItem after, TaskCalendarDestination destination)
    {
        bool hadAny = before.DueOn is not null || before.Reminders is { Count: > 0 };
        bool hasAny = after.DueOn is not null || after.Reminders is { Count: > 0 };
        return !hadAny && hasAny ? after with { Calendar = destination == TaskCalendarDestination.GoogleCalendar } : after;
    }
}
