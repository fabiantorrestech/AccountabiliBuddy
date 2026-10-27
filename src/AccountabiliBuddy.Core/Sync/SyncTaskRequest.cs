namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// The <see cref="SyncAction.Text"/> of a <see cref="SyncAction.KindTaskAdd"/>, <see cref="SyncAction.KindTaskEdit"/> or
/// <see cref="SyncAction.KindTaskReorder"/> action (ADR 48), as JSON. Which fields count depends on the kind:
/// <list type="bullet">
/// <item>taskAdd (<see cref="SyncAction.Item"/> = the new task's id, chosen by the phone): <paramref name="Title"/>, optionally a category
/// (<paramref name="CategoryId"/>, else <paramref name="Category"/> by name with <paramref name="CategoryColor"/> for a new one) and <paramref name="Steps"/>.</item>
/// <item>taskEdit (<see cref="SyncAction.Item"/> = the task's id): <paramref name="Title"/> (null keeps it) and <paramref name="Steps"/> (null keeps them; a list
/// replaces them: steps left out are removed, ticked ones keep their tick).</item>
/// <item>taskReorder (no item): <paramref name="Order"/>, the task ids in the new order (within a category; tasks left out keep their place after the listed ones).</item>
/// </list>
/// </summary>
public sealed record SyncTaskRequest(
    string? Title = null,
    Guid? CategoryId = null,
    string? Category = null,
    string? CategoryColor = null,
    IReadOnlyList<SyncTaskStep>? Steps = null,
    IReadOnlyList<Guid>? Order = null);
