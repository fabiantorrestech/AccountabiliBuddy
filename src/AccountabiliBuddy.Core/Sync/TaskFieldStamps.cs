using System.Text.Json.Serialization;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// A task's per-part stamps (ADR 76), one per <see cref="TaskField"/>. A missing stamp counts as made by nobody when the task was
/// created, older than any edit.
/// </summary>
public sealed record TaskFieldStamps(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TaskFieldStamp? Title = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TaskFieldStamp? Note = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TaskFieldStamp? Steps = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TaskFieldStamp? Label = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TaskFieldStamp? Tags = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TaskFieldStamp? Bin = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TaskFieldStamp? Reminders = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TaskFieldStamp? Estimate = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TaskFieldStamp? Due = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TaskFieldStamp? Highlight = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TaskFieldStamp? CantMiss = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TaskFieldStamp? Devices = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TaskFieldStamp? Calendar = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TaskFieldStamp? Number = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TaskFieldStamp? Plan = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TaskFieldStamp? Goal = null)
{
    public static TaskFieldStamps None { get; } = new();

    /// <summary>
    /// Every field stamped the same (a new task, or one the migration made), except the goal link (GS5): it is stamped when it is first set, so a
    /// task that never carried a goal writes no <c>goal</c> stamp (older readers and the phone's twin see the same text as before). An unstamped
    /// goal counts as made when the task was created, older than any link.
    /// </summary>
    public static TaskFieldStamps AllAt(TaskFieldStamp stamp)
    {
        return new TaskFieldStamps(stamp, stamp, stamp, stamp, stamp, stamp, stamp, stamp, stamp, stamp, stamp, stamp, stamp, stamp, stamp);
    }

    /// <summary>
    /// <see cref="AllAt"/> for a task that may already carry a goal link (a copy, a task put back whole, a restore, one built from an older
    /// device's item): the link is stamped with the rest, so it counts as set then; with no link there is no goal stamp.
    /// </summary>
    public static TaskFieldStamps ForNew(TaskItem task, TaskFieldStamp stamp)
    {
        return AllAt(stamp) with { Goal = task.GoalId is null ? null : stamp };
    }

    /// <summary>The newest of the stamps; null when there are none.</summary>
    [JsonIgnore]
    public DateTimeOffset? Newest => Enum.GetValues<TaskField>().Select(Of).Where(stamp => stamp is not null).Select(stamp => (DateTimeOffset?)stamp!.At).Max();

    public TaskFieldStamp? Of(TaskField field)
    {
        return field switch
        {
            TaskField.Title => Title,
            TaskField.Note => Note,
            TaskField.Steps => Steps,
            TaskField.Label => Label,
            TaskField.Tags => Tags,
            TaskField.Bin => Bin,
            TaskField.Reminders => Reminders,
            TaskField.Estimate => Estimate,
            TaskField.Due => Due,
            TaskField.Highlight => Highlight,
            TaskField.CantMiss => CantMiss,
            TaskField.Devices => Devices,
            TaskField.Calendar => Calendar,
            TaskField.Number => Number,
            TaskField.Plan => Plan,
            TaskField.Goal => Goal,
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
    }

    public TaskFieldStamps With(TaskField field, TaskFieldStamp? stamp)
    {
        return field switch
        {
            TaskField.Title => this with { Title = stamp },
            TaskField.Note => this with { Note = stamp },
            TaskField.Steps => this with { Steps = stamp },
            TaskField.Label => this with { Label = stamp },
            TaskField.Tags => this with { Tags = stamp },
            TaskField.Bin => this with { Bin = stamp },
            TaskField.Reminders => this with { Reminders = stamp },
            TaskField.Estimate => this with { Estimate = stamp },
            TaskField.Due => this with { Due = stamp },
            TaskField.Highlight => this with { Highlight = stamp },
            TaskField.CantMiss => this with { CantMiss = stamp },
            TaskField.Devices => this with { Devices = stamp },
            TaskField.Calendar => this with { Calendar = stamp },
            TaskField.Number => this with { Number = stamp },
            TaskField.Plan => this with { Plan = stamp },
            TaskField.Goal => this with { Goal = stamp },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
    }
}
