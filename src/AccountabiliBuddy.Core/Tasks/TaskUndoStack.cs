namespace AccountabiliBuddy.Core.Tasks;

/// <summary>The last <see cref="Capacity"/> task actions, newest last (Ctrl+Z still undoes after the toast is gone, spec 05 section 14). Not thread-safe.</summary>
public sealed class TaskUndoStack
{
    public const int Capacity = 10;

    private readonly List<TaskUndoEntry> _entries = [];

    public int Count => _entries.Count;

    /// <summary>The newest entry, or null when there is none.</summary>
    public TaskUndoEntry? Peek => _entries.Count == 0 ? null : _entries[^1];

    /// <summary>Keeps <paramref name="entry"/> as the newest; the oldest goes past <see cref="Capacity"/>.</summary>
    public void Push(TaskUndoEntry entry)
    {
        _entries.Add(entry);
        if (_entries.Count > Capacity)
        {
            _entries.RemoveAt(0);
        }
    }

    /// <summary>Takes the newest entry off; false when there is none.</summary>
    public bool TryPop(out TaskUndoEntry? entry)
    {
        entry = Peek;
        if (entry is null)
        {
            return false;
        }

        _entries.RemoveAt(_entries.Count - 1);
        return true;
    }

    /// <summary>
    /// Drops every entry that touched task <paramref name="id"/>: a change not on the stack (a "Changed on both devices" pick) came
    /// after them, and undoing one would bring back the text the pick replaced.
    /// </summary>
    /// <returns>How many entries were dropped.</returns>
    public int Forget(Guid id)
    {
        return _entries.RemoveAll(entry => entry.Tasks.ContainsKey(id));
    }

    public void Clear()
    {
        _entries.Clear();
    }
}
