using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>What a view shows: its sections in order. A task can be in more than one section when grouped by tag.</summary>
public sealed record TaskViewResult(IReadOnlyList<TaskViewGroup> Groups)
{
    /// <summary>How many different tasks show ("Show N tasks").</summary>
    public int Count => Groups.SelectMany(group => group.Tasks).Select(task => task.Id).Distinct().Count();

    public bool IsEmpty => Count == 0;

    /// <summary>Every task shown, in section order, each once.</summary>
    public IReadOnlyList<TaskItem> Tasks => Groups.SelectMany(group => group.Tasks).DistinctBy(task => task.Id).ToList();
}
