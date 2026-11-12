using System.Collections;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// A nested goal list as read (a goal's source rules, a review's decisions): it lists only the items this app knows, and carries the
/// newer ones (<see cref="GoalNewerItem"/>) so they are written back (<see cref="GoalNewerListConverter{T}"/>). Code that builds a new
/// list leaves them out; the store puts them back (<see cref="GoalUnknowns"/>).
/// </summary>
public sealed class GoalNewerList<T> : IReadOnlyList<T>
{
    public GoalNewerList(IReadOnlyList<T> known, IReadOnlyList<GoalNewerItem> newer)
    {
        Known = known;
        Newer = newer;
    }

    /// <summary>The items this app knows: what the list enumerates.</summary>
    public IReadOnlyList<T> Known { get; }

    /// <summary>The items kept as written.</summary>
    public IReadOnlyList<GoalNewerItem> Newer { get; }

    public int Count => Known.Count;

    public T this[int index] => Known[index];

    public IEnumerator<T> GetEnumerator()
    {
        return Known.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
