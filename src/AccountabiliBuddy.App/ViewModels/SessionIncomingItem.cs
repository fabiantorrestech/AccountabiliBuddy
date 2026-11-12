namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// What wants a place in a full This session (the "This session is full" dialog's highlighted card): a task (moved, added, pulled from a
/// list, or "Work on it now" from a reminder) or a running task's step brought into focus.
/// </summary>
/// <param name="TaskId">The task coming in, or the task whose step comes into focus.</param>
/// <param name="StepId">The step brought into focus; null for a whole task.</param>
/// <param name="Title">The task's (or step's) title; shown only.</param>
/// <param name="Detail">"● Label · 25 min · from Today · Queued" without the dot.</param>
/// <param name="LabelColor">The label's colour (#RRGGBB) for the dot; null for none.</param>
public sealed record SessionIncomingItem(Guid TaskId, Guid? StepId, string Title, string Detail, string? LabelColor);
