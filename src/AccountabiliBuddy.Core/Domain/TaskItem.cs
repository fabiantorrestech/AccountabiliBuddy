using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// A task in the one shared task list (ADR 76): every task the user has, work or personal, sits in one <see cref="TaskBin"/>.
/// Replaces the next session's list, Later, come-backs and Auto-saved as separate lists. The text (title, steps, note, tags,
/// origin, the other version of a conflict) is the user's private text: sealed wherever it is stored or synced, never logged.
/// A running session's checklist stays a <see cref="SessionChecklist"/> of <see cref="SessionTask"/>s.
/// </summary>
/// <param name="Bin">Where it sits.</param>
/// <param name="Position">Its place within its bin: lower first; fractions put it between two others.</param>
/// <param name="CreatedAt">When it was made (numbers follow this order).</param>
/// <param name="EditedAt">Its newest change of any field: the shared lists' item stamp (ADR 48).</param>
/// <param name="TouchedAt">Any edit, move, step tick or opening its details; drives "untouched N wk".</param>
/// <param name="Number">Its number ("142"); null until one is given.</param>
/// <param name="FormerNumbers">Temporary numbers it had before its final one, so "143b" written then still finds it.</param>
/// <param name="PrevBin">Where it was before <see cref="TaskBin.Done"/>; it goes back there when reopened.</param>
/// <param name="LabelId">Its label (the coloured dot, at most one); null for "No label". See <see cref="TaskLabel.IdFor"/>.</param>
/// <param name="Tags">Its tags (ADR 72's labels, renamed): plain names, compared case-insensitively, see <see cref="TaskLabels"/>.</param>
/// <param name="Steps">Its steps in order; null for none.</param>
/// <param name="EstimateMinutes">"Time limit"; null for none.</param>
/// <param name="Effort">Reserved for U0 to U4 ("Coming soon"): never set yet.</param>
/// <param name="Note">The private note; null for none.</param>
/// <param name="DueOn">"Done by" date; null for none.</param>
/// <param name="DueAt">"Done by" time on <paramref name="DueOn"/>; null for the whole day.</param>
/// <param name="Reminders">Up to <see cref="TaskReminder.MaxPerTask"/>; null for none.</param>
/// <param name="CantMiss">Its reminders ring full screen and must be held to dismiss.</param>
/// <param name="Highlight">Star and tint.</param>
/// <param name="Origin">Where it came from, as shown ("Added from phone", "Parked Tue 11/3 11:52 AM"); null for a task typed in place.</param>
/// <param name="DoneAt">When it entered <see cref="TaskBin.Done"/>; null while open.</param>
/// <param name="Devices">The devices it syncs to; null for all. A single device is ADR 72's local item kept on that device.</param>
/// <param name="Calendar">Its reminders and due date go to the calendar (when calendar export exists).</param>
/// <param name="PlannedFor">The day part it was put in the plan for (ADR 50, "Put in plan"); kept so the day-part rule still works.</param>
/// <param name="DeletedAt">Set on a tombstone: when it was deleted. A tombstone keeps no text.</param>
/// <param name="Conflicts">"Changed on both devices" versions waiting for the user's pick; null for none.</param>
/// <param name="AutoSavedFor">
/// For a To sort task that came from Auto-saved (ADR 72): the plan day its shutdown card wrapped up. While it is open and within
/// Auto-saved's 30 days it travels to older apps in the old <c>autoSaved</c> field (not <c>parked</c>), so it shows once there.
/// </param>
public sealed record TaskItem(
    Guid Id,
    string Title,
    TaskBin Bin,
    double Position,
    DateTimeOffset CreatedAt,
    DateTimeOffset EditedAt,
    DateTimeOffset TouchedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TaskNumber? Number = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<int>? FormerNumbers = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TaskBin? PrevBin = null,
    [property: JsonPropertyName("label")][property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? LabelId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Tags = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<TaskStep>? Steps = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? EstimateMinutes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Effort = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Note = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? DueOn = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TimeOnly? DueAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<TaskReminder>? Reminders = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool CantMiss = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Highlight = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Origin = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? DoneAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<Guid>? Devices = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Calendar = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PlannedSlot? PlannedFor = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? DeletedAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<TaskConflict>? Conflicts = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? AutoSavedFor = null)
{
    /// <summary>Neither a tombstone nor a local item's stub.</summary>
    [JsonIgnore]
    public bool IsLive => DeletedAt is null;

    [JsonIgnore]
    public bool IsDone => DoneAt is not null;

    /// <summary>The one device that keeps it, when <see cref="Devices"/> names exactly one (ADR 72's local item); null otherwise.</summary>
    [JsonIgnore]
    public Guid? LocalTo => Devices is { Count: 1 } ? Devices[0] : null;

    /// <summary>
    /// How many of the session's places it takes (ADR 76): one per focused open step, or one for a task with none focused
    /// (a task without steps is one action).
    /// </summary>
    [JsonIgnore]
    public int ActionCount
    {
        get
        {
            int focused = (Steps ?? []).Count(step => step.Focus && !step.IsDone);
            return focused > 0 ? focused : 1;
        }
    }

    /// <summary>
    /// The task as a tombstone: every text and user field gone, deleted at <paramref name="at"/>. It keeps its bin and
    /// <see cref="AutoSavedFor"/> (so an old sync field knows which list to delete it from), its creation time and its devices.
    /// </summary>
    public TaskItem Deleted(DateTimeOffset at)
    {
        return new TaskItem(Id, string.Empty, Bin, Position, CreatedAt, at, at, PrevBin: PrevBin, Devices: Devices, DeletedAt: at, AutoSavedFor: AutoSavedFor);
    }
}
