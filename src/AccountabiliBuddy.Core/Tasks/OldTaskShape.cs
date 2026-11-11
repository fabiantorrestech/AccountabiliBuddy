using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// One item of an old sync field (ADR 48, 50, 72) in one shape, whichever list it came from, with what that list can carry, so
/// <see cref="OldTaskFieldsReader"/> compares and applies it the same way for every list.
/// </summary>
internal sealed record OldTaskShape(
    OldTaskList List,
    Guid Id,
    string Title,
    DateTimeOffset? DoneAt,
    string? Category,
    string? CategoryColor,
    IReadOnlyList<SessionSubtask>? Subtasks,
    int? EstimateMinutes,
    string? Note,
    DateOnly? DueOn,
    PlannedSlot? PlannedFor,
    IReadOnlyList<string>? Labels,
    Guid? LocalTo,
    DateTimeOffset EditedAt,
    DateTimeOffset? DeletedAt,
    double Position,
    DateTimeOffset? CreatedAt,
    string? BinMarker,
    bool HasDetails,
    bool HasDueDate,
    bool IsLegacyComeback = false,
    DateOnly? AutoSavedFor = null)
{
    public bool IsLive => DeletedAt is null;

    /// <summary>A local item's stub (ADR 72): kept on <see cref="LocalTo"/>, not deleted.</summary>
    public bool IsStub => DeletedAt is not null && LocalTo is not null;

    public static OldTaskShape Of(SyncNextTask item)
    {
        return new OldTaskShape(
            OldTaskList.NextTasks, item.Id, item.Title ?? string.Empty, item.DoneAt, item.Category, item.CategoryColor, item.Subtasks,
            item.EstimateMinutes, item.Note, null, item.PlannedFor, item.Labels, item.LocalTo, item.EditedAt, item.DeletedAt,
            item.Position, null, null, HasDetails: true, HasDueDate: false);
    }

    public static OldTaskShape Of(SyncLaterItem item)
    {
        return new OldTaskShape(
            OldTaskList.Later, item.Id, item.Title ?? string.Empty, item.DoneAt, item.Category, item.CategoryColor, item.Subtasks,
            item.EstimateMinutes, item.Note, item.DueOn, item.PlannedFor, item.Labels, item.LocalTo, item.EditedAt, item.DeletedAt,
            item.Position, null, item.Bin, HasDetails: true, HasDueDate: true);
    }

    public static OldTaskShape Of(SyncParked item)
    {
        return new OldTaskShape(
            OldTaskList.Parked, item.Id, item.Text ?? string.Empty, item.DoneAt, item.Category, item.CategoryColor, null,
            null, null, null, null, item.Labels, item.LocalTo, item.EditedAt, item.DeletedAt,
            item.Position, item.CreatedAt, null, HasDetails: false, HasDueDate: false);
    }

    public static OldTaskShape Of(SyncAutoSavedItem item)
    {
        return new OldTaskShape(
            OldTaskList.AutoSaved, item.Id, item.Title ?? string.Empty, null, item.Category, item.CategoryColor, null,
            null, null, null, null, item.Labels, item.LocalTo, item.EditedAt, item.DeletedAt,
            item.Position, item.SavedAt, null, HasDetails: false, HasDueDate: false, AutoSavedFor: item.WrapUpOf);
    }

    /// <summary>A legacy open come-back: text and creation time only; it never changes a task this device has.</summary>
    public static OldTaskShape Of(SyncComeback item)
    {
        return new OldTaskShape(
            OldTaskList.Parked, item.Id, item.Text ?? string.Empty, null, null, null, null,
            null, null, null, null, null, null, item.CreatedAt, null,
            0, item.CreatedAt, null, HasDetails: false, HasDueDate: false, IsLegacyComeback: true);
    }

    /// <summary>This device's task as <paramref name="list"/> would carry it (what an older app last saw of it, at best).</summary>
    public static OldTaskShape Projected(TaskItem task, OldTaskList list, IReadOnlyList<TaskLabel> labels)
    {
        return list switch
        {
            OldTaskList.NextTasks => Of(OldTaskFieldsWriter.ToNextTask(task, labels)),
            OldTaskList.Later => Of(OldTaskFieldsWriter.ToLaterItem(task, labels)),
            OldTaskList.AutoSaved => Of(OldTaskFieldsWriter.ToAutoSaved(task, labels)),
            _ => Of(OldTaskFieldsWriter.ToParked(task, labels)),
        };
    }
}
