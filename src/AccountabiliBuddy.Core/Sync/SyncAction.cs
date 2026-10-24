namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// Something the user did on the phone for a PC (ADR 26): tick a task, park a thought, tick a come-back, step away or
/// come back. The phone keeps it in its own sealed file until the PC it's meant for lists its id in
/// <see cref="SyncFile.Acks"/>; each PC applies an action once.
/// </summary>
/// <param name="Id">Unique; the PC acknowledges by it.</param>
/// <param name="Kind">One of the <c>Kind…</c> constants; a PC ignores kinds it doesn't know.</param>
/// <param name="Target">The device the action is for (its sync device id).</param>
/// <param name="Item">The task, subtask or come-back it's about (tick kinds).</param>
/// <param name="Text">The thought to park (park).</param>
/// <param name="At">When it was done on the phone.</param>
public sealed record SyncAction(Guid Id, string Kind, Guid Target, Guid? Item, string? Text, DateTimeOffset At)
{
    public const string KindTickTask = "tickTask";
    public const string KindPark = "park";
    public const string KindTickComeback = "tickComeback";
    public const string KindStepAway = "stepAway";
    public const string KindBack = "back";
}
