namespace AccountabiliBuddy.Core.Domain;

/// <summary>One step in a task's new list of steps (<see cref="SessionChecklist.Edit"/>): an id of the task's own steps keeps that step; anything else is a new step.</summary>
public sealed record SessionStepEdit(Guid? Id, string Title);
