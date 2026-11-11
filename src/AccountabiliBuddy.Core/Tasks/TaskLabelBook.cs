using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// The labels while a migration or an old-field read works through items that only name their category (ADR 76): finds a label
/// by name (case-insensitive) and makes one, with the name-based id, when a name is new.
/// </summary>
internal sealed class TaskLabelBook
{
    private readonly List<TaskLabel> _labels;

    public TaskLabelBook(IEnumerable<TaskLabel> labels)
    {
        _labels = labels.ToList();
    }

    public IReadOnlyList<TaskLabel> Labels => _labels;

    /// <summary>How many labels <see cref="IdForName"/> made.</summary>
    public int Made { get; private set; }

    /// <summary>The live label with this name.</summary>
    public TaskLabel? Named(string name)
    {
        return _labels.FirstOrDefault(label => label.IsLive && TaskLabel.SameName(label.Name, name));
    }

    public TaskLabel? Find(Guid? id)
    {
        return id is Guid labelId ? _labels.FirstOrDefault(label => label.Id == labelId && label.IsLive) : null;
    }

    /// <summary>
    /// The id of the label named <paramref name="name"/>; a new label (colour <paramref name="color"/> when it is valid, else the
    /// first unused one; stamped <paramref name="stamp"/>) when there is none. Null for no name.
    /// </summary>
    public Guid? IdForName(string? name, string? color, DateTimeOffset stamp)
    {
        if (name is null || name.Trim().Length == 0)
        {
            return null;
        }

        if (Named(name) is TaskLabel held)
        {
            return held.Id;
        }

        Guid id = TaskLabel.IdFor(name);
        string chosen = TaskLabelColors.IsValid(color) ? color!.Trim() : TaskLabelColors.FirstUnused(_labels);
        double position = _labels.Count == 0 ? 1 : _labels.Max(label => label.Position) + 1;
        int existing = _labels.FindIndex(label => label.Id == id);
        var label = new TaskLabel(id, name.Trim(), chosen, position, stamp);
        if (existing >= 0)
        {
            // A tombstone of the same name: a newer use brings the label back.
            _labels[existing] = label with { EditedAt = stamp > _labels[existing].EditedAt ? stamp : _labels[existing].EditedAt.AddTicks(1) };
        }
        else
        {
            _labels.Add(label);
        }

        Made++;
        return id;
    }

    /// <summary>Adds or replaces a label by id.</summary>
    public void Put(TaskLabel label)
    {
        int index = _labels.FindIndex(other => other.Id == label.Id);
        if (index >= 0)
        {
            _labels[index] = label;
        }
        else
        {
            _labels.Add(label);
        }
    }
}
