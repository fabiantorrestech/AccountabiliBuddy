namespace AccountabiliBuddy.App.ViewModels;

/// <summary>What the Tasks page has selected: a row's list and its id.</summary>
public readonly record struct TaskRowKey(TaskRowKind Kind, Guid Id);
