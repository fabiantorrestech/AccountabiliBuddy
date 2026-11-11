using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// A step of a <see cref="TaskItem"/> (ADR 76). Its letter (a, b ... z, aa ...) is its place in the list, never stored
/// (<see cref="TaskStepLetters"/>).
/// </summary>
/// <param name="DoneAt">When it was ticked; null while it is open.</param>
/// <param name="Focus">It is one of the session's actions (counts toward the session cap while its task is in This session).</param>
/// <param name="EstimateMinutes">Its time limit in minutes; null for none.</param>
public sealed record TaskStep(
    Guid Id,
    string Title,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? DoneAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Focus = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? EstimateMinutes = null)
{
    [JsonIgnore]
    public bool IsDone => DoneAt is not null;

    /// <summary>The step as a running checklist's subtask (which has no focus or estimate).</summary>
    public SessionSubtask ToSubtask()
    {
        return new SessionSubtask(Id, Title, DoneAt);
    }
}
