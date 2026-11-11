using System.Text.Json.Serialization;

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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TaskFieldStamp? Plan = null)
{
    public static TaskFieldStamps None { get; } = new();

    /// <summary>Every field stamped the same (a new task, or one the migration made).</summary>
    public static TaskFieldStamps AllAt(TaskFieldStamp stamp)
    {
        return new TaskFieldStamps(stamp, stamp, stamp, stamp, stamp, stamp, stamp, stamp, stamp, stamp, stamp, stamp, stamp, stamp, stamp);
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
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
    }
}
