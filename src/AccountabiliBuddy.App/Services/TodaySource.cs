using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// What a task brought to Today carries from where it was (Later, a come-back, an ended session's record): its title, steps
/// (they come back open), category (by name and colour, as the Later list and history keep them), estimate, note and labels (ADR 72). Private text.
/// </summary>
/// <param name="Id">The task's id in the task list when it moves from one of its lists (ADR 76): it keeps it (and its steps keep theirs and
/// their ticks); null for a copy with a new id.</param>
public sealed record TodaySource(
    string Title,
    IReadOnlyList<SessionSubtask> Steps,
    string? Category = null,
    string? CategoryColor = null,
    int? EstimateMinutes = null,
    string? Note = null,
    IReadOnlyList<string>? Labels = null,
    Guid? Id = null);
