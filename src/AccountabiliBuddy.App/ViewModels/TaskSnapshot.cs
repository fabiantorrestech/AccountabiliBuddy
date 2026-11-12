using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// What the Tasks page's detail card shows of one item, read from whichever store holds it (the checklist, the Later list or
/// the come-backs). Private text: never logged.
/// </summary>
/// <param name="KindText">The Muted line over the title, e.g. "In this session · 1 of 3", "Later · due Fri", "Come back to".</param>
/// <param name="IsRunningTask">It is the task the running session is on.</param>
/// <param name="IsDone">Ticked: shown, not changed.</param>
/// <param name="Item">The task as the task list holds it (label, tags, highlight, due, reminders, number); null when it holds none.</param>
/// <param name="FocusedSteps">The steps in focus (the session's actions).</param>
public sealed record TaskSnapshot(
    TaskRowKey Key,
    string KindText,
    string Title,
    IReadOnlyList<SessionSubtask> Steps,
    int? EstimateMinutes,
    string? Note,
    DateOnly? DueOn,
    PlannedSlot? PlannedFor,
    bool IsRunningTask,
    bool IsDone,
    string? ParkedText,
    Core.Domain.TaskItem? Item = null,
    IReadOnlySet<Guid>? FocusedSteps = null);
