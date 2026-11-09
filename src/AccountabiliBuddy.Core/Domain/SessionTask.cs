using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Domain;

/// <summary>A task in a guided session's checklist (ADR 24), with its subtasks.</summary>
/// <param name="DoneAt">When it was ticked, or when its last subtask was; null while it's open.</param>
/// <param name="CategoryId">The <see cref="TaskCategory"/> it's in; null = no category (listed last).</param>
/// <param name="Category">
/// Its category's name as it was sent to paired devices or kept in history (filled by <see cref="SessionChecklist.Labelled"/>);
/// null in the running checklist, which goes by <paramref name="CategoryId"/>.
/// </param>
/// <param name="CategoryColor">Its category's colour, "#RRGGBB", alongside <paramref name="Category"/>.</param>
/// <param name="EstimateMinutes">How long the user expects it to take (ADR 50); null = no estimate.</param>
/// <param name="Note">
/// The user's private note on it (ADR 50): sealed wherever it is stored, never logged, sent only inside the sealed sync
/// files to the user's own devices; null = none.
/// </param>
/// <param name="PlannedFor">The day part it was put in the plan for ("Put in plan", ADR 50); null = not planned.</param>
/// <param name="Labels">Its labels (ADR 72): plain names, see <see cref="TaskLabels"/>; null = none.</param>
/// <param name="LocalTo">
/// In a PC's task history only (ADR 72): the device that keeps this past task as a local item; null = it syncs. On every other
/// device, and in the sync file, such a task is a stub with no text.
/// </param>
public sealed record SessionTask(
    Guid Id,
    string Title,
    DateTimeOffset? DoneAt,
    IReadOnlyList<SessionSubtask> Subtasks,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? CategoryId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Category = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CategoryColor = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? EstimateMinutes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Note = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PlannedSlot? PlannedFor = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Labels = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? LocalTo = null)
{
    public bool IsDone => DoneAt is not null;

    /// <summary>The task as a local stub (ADR 72): its id and its keeper only, no text.</summary>
    public SessionTask AsLocalStub(Guid keeper)
    {
        return new SessionTask(Id, string.Empty, null, [], LocalTo: keeper);
    }
}
