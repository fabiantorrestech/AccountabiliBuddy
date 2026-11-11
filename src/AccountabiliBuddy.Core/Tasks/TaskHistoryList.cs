using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// History as the screens list it (spec 05 section 10): the last 30 days, newest first, in its two lists. "Do again" belongs to the
/// finished ones, "Bring back" to the archived and deleted ones. Holds the user's task text: shown, never logged.
/// </summary>
/// <param name="Finished">Finished tasks.</param>
/// <param name="Removed">Archived and deleted tasks.</param>
public sealed record TaskHistoryList(IReadOnlyList<TaskHistoryEntry> Finished, IReadOnlyList<TaskHistoryEntry> Removed)
{
    public bool IsEmpty => Finished.Count == 0 && Removed.Count == 0;
}
