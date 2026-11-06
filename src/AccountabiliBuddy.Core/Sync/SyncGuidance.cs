namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// The guidance mode a device chose (ADR 68): the mode and when it was chosen, nothing else. The newest edit wins on every
/// device; a looser one (<see cref="ModeCompanion"/>) applies only after the receiver's own looser-change delay, counted from
/// <paramref name="EditedAt"/>, and never during a session there.
/// </summary>
/// <param name="Mode">One of the <c>Mode…</c> constants; a reader ignores a mode it doesn't know.</param>
/// <param name="EditedAt">When it was chosen.</param>
public sealed record SyncGuidance(string Mode, DateTimeOffset EditedAt)
{
    public const string ModeGuarding = "guarding";
    public const string ModeCompanion = "companion";
}
