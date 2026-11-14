using System.Text.Json.Serialization;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// One checklist of a PC's 90-day task history as its sealed sync file carries it (ADR 72): a past session's tasks, or a day's
/// tasks ticked with no session running (<see cref="Outside"/>). Each task carries its id, title, steps, done time, category
/// name and colour, estimate, note and labels; never a session note or reason. A task kept on one device
/// (<see cref="SessionTask.LocalTo"/>) is a stub with no text. Receivers show it under "Unfinished from" and in the Archive,
/// and never count it as a session.
/// </summary>
/// <param name="SessionStart">When the session started; null for an <see cref="Outside"/> record.</param>
/// <param name="Outside">The tasks were ticked outside any session (a Later task ticked off).</param>
/// <param name="Deleted">
/// Past tasks of this session its owner deleted in the last 30 days (<see cref="SyncDeletedPastTask"/>); optional, so an older app, which
/// ignores it, simply doesn't list them (AR1b review). Never in <see cref="Tasks"/>, so no app counts or shows one as live.
/// </param>
public sealed record SyncTaskHistoryEntry(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? SessionStart,
    IReadOnlyList<SessionTask> Tasks,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Outside = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SyncDeletedPastTask>? Deleted = null)
{
    /// <summary>How long history is kept and sent.</summary>
    public static readonly TimeSpan Kept = TimeSpan.FromDays(90);

    /// <summary>The time the entry stands at: its session start, else its first tick; null for an outside record with no tick.</summary>
    [JsonIgnore]
    public DateTimeOffset? At => SessionStart ?? Tasks.Where(task => task.DoneAt is not null).Select(task => task.DoneAt).Min();
}
