namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// What a pure task action (<see cref="TaskActions"/>, <see cref="TaskHistoryRules"/>) made of the lists: the new lists, the toast
/// text and the ids of every task (and History entry) it touched or made, which is what an undo restores. Nothing touched means
/// nothing happened. The toast holds the user's task titles: shown, never logged.
/// </summary>
/// <param name="Lists">The lists after the action (the same lists when nothing happened).</param>
/// <param name="Toast">E.g. "Finished “Draft Q4…”"; empty when nothing happened.</param>
/// <param name="Affected">The ids the action touched or made.</param>
public sealed record TaskActionResult(TaskLists Lists, string Toast, IReadOnlyList<Guid> Affected)
{
    public bool Changed => Affected.Count > 0;

    public static TaskActionResult None(TaskLists lists)
    {
        return new TaskActionResult(lists, string.Empty, []);
    }
}
