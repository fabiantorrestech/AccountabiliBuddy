namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// Something the user did on the phone for a PC (ADR 26): tick a task, park a thought, tick a come-back, step away or
/// come back; and (ADR 42) start a session, end one early in two steps, or edit the day plan. The phone keeps it in its
/// own sealed file until the PC it's meant for lists its id in <see cref="SyncFile.Acks"/>; each PC applies an action
/// once and, since ADR 42, answers it in <see cref="SyncFile.ActionResults"/>.
/// </summary>
/// <param name="Id">Unique; the PC acknowledges by it.</param>
/// <param name="Kind">One of the <c>Kind…</c> constants; a PC answers kinds it doesn't know with "not supported".</param>
/// <param name="Target">The device the action is for (its sync device id).</param>
/// <param name="Item">The task, subtask or come-back it's about (tick kinds, task edit; the new task's id for task add); the offer's request id (end early).</param>
/// <param name="Text">
/// The thought to park (park); a <see cref="SyncStartRequest"/> as JSON (start); the reason's length only, e.g. "24"
/// (end early); a <see cref="SyncDayPlanEditRequest"/> as JSON (day plan edit); a <see cref="SyncNoiseRequest"/> as JSON (noise);
/// a <see cref="SyncTaskRequest"/> as JSON (task add, edit, reorder).
/// </param>
/// <param name="At">When it was done on the phone.</param>
public sealed record SyncAction(Guid Id, string Kind, Guid Target, Guid? Item, string? Text, DateTimeOffset At)
{
    public const string KindTickTask = "tickTask";
    public const string KindPark = "park";
    public const string KindTickComeback = "tickComeback";
    public const string KindStepAway = "stepAway";
    public const string KindBack = "back";

    /// <summary>Start a session (ADR 42); taken only within 2 minutes.</summary>
    public const string KindStart = "start";

    /// <summary>Ask to end the running session early (ADR 42): the PC answers with a <see cref="SyncEndEarlyOffer"/>; taken only within 2 minutes.</summary>
    public const string KindEndEarlyBegin = "endEarlyBegin";

    /// <summary>End early after the offer's wait (ADR 42); taken until the offer's allowed-from time plus 10 minutes.</summary>
    public const string KindEndEarly = "endEarly";

    /// <summary>Change the day plan (ADR 42, 19); taken only within a day.</summary>
    public const string KindDayPlanEdit = "dayPlanEdit";

    /// <summary>Control the PC's noise player (ADR 42, 36); taken only within 2 minutes, never late.</summary>
    public const string KindNoise = "noise";

    /// <summary>Add a task to the running session's checklist (ADR 48); a 4th is parked; taken only within 2 minutes.</summary>
    public const string KindTaskAdd = "taskAdd";

    /// <summary>Rename a task or replace its steps in the running session's checklist (ADR 48); taken only within 2 minutes.</summary>
    public const string KindTaskEdit = "taskEdit";

    /// <summary>Put the running session's tasks in a new order (ADR 48); taken only within 2 minutes.</summary>
    public const string KindTaskReorder = "taskReorder";
}
