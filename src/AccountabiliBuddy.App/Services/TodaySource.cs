using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// What a task brought to Today carries from where it was (Later, a come-back, an ended session's record): its title, steps
/// (they come back open), category (by name and colour, as the Later list and history keep them), estimate and note. Private text.
/// </summary>
public sealed record TodaySource(
    string Title,
    IReadOnlyList<SessionSubtask> Steps,
    string? Category = null,
    string? CategoryColor = null,
    int? EstimateMinutes = null,
    string? Note = null);
