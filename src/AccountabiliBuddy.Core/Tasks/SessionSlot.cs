namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// One of the running session's places (ADR 76): a stepless task, a step in focus, or a task with steps and none in focus (its
/// heading holds the place). Numbered 1, 2, 3 in the checklist's order; a finished task holds its place but has no number.
/// </summary>
/// <param name="Number">Its number in This session, from 1.</param>
/// <param name="TaskId">The task it belongs to.</param>
/// <param name="StepId">The focused step it is; null for the task itself.</param>
public sealed record SessionSlot(int Number, Guid TaskId, Guid? StepId);
