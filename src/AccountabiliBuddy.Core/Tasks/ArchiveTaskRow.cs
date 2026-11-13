namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// One task of the Archive (done, or left open when its session ended), the same on every device (AR1-fix). Holds the user's task
/// text: shown, never logged.
/// </summary>
/// <param name="DoneAt">When it was done; null while open.</param>
/// <param name="SessionStart">When its session started; null for a "done outside a session" record or a task of the task list.</param>
/// <param name="DeviceId">The PC it came from (received rows); null otherwise.</param>
/// <param name="DeviceName">What the row says after "on" (received rows); null for this PC's own and the task list's.</param>
/// <param name="Sort">The time the row is ordered by, newest first.</param>
/// <param name="CanTick">The check box is offered (tick, untick).</param>
/// <param name="CanDelete">Delete is offered.</param>
/// <param name="CanEdit">Rename, steps and the like are offered: only a row of this PC's own history, whose text this PC owns.</param>
public sealed record ArchiveTaskRow(
    Guid Id,
    string Title,
    DateTimeOffset? DoneAt,
    DateTimeOffset? SessionStart,
    bool Outside,
    ArchiveSource Source,
    Guid? DeviceId,
    string? DeviceName,
    Guid? CategoryId,
    string? Category,
    string? CategoryColor,
    IReadOnlyList<ArchiveStepRow> Steps,
    DateTimeOffset Sort,
    bool CanTick,
    bool CanDelete,
    bool CanEdit)
{
    public bool IsDone => DoneAt is not null;
}
