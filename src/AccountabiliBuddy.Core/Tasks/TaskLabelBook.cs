using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// The labels while a migration or an old-field read works through items that only name their category (ADR 76): finds a label
/// by name (case-insensitive) and makes one, with the name-based id, when a name is new.
/// </summary>
internal sealed class TaskLabelBook
{
    private readonly List<TaskLabel> _labels;
    private readonly Guid _reviver;

    /// <param name="reviver">The device a revived label's fresh "Rings on" stamp names (the sender of the items being read).</param>
    public TaskLabelBook(IEnumerable<TaskLabel> labels, Guid reviver = default)
    {
        _labels = labels.ToList();
        _reviver = reviver;
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

        Guid id = FreeIdFor(name, _labels);
        string chosen = TaskLabelColors.IsValid(color) ? color!.Trim() : TaskLabelColors.FirstUnused(_labels);
        double position = _labels.Count == 0 ? 1 : _labels.Max(label => label.Position) + 1;
        int existing = _labels.FindIndex(label => label.Id == id);
        var label = new TaskLabel(id, name.Trim(), chosen, position, stamp);
        if (existing >= 0)
        {
            // A tombstone (FreeIdFor never gives a live label's id): a newer use brings the label back, ringing on every device, stamped
            // at the revive, so a device still holding the old label's ring list can't hand it back (TaskLabelMerge).
            DateTimeOffset revived = stamp > _labels[existing].EditedAt ? stamp : _labels[existing].EditedAt.AddTicks(1);
            _labels[existing] = label with { EditedAt = revived, RingOn = null, RingOnStamp = new Sync.TaskFieldStamp(revived, _reviver) };
        }
        else
        {
            _labels.Add(label);
        }

        Made++;
        return id;
    }

    /// <summary>
    /// When a label that once had <paramref name="name"/> was last deleted or renamed: the newest change of the labels holding the name's
    /// ids (<see cref="TaskLabel.IdFor"/>, then "#2", "#3"... while one exists) that aren't a live label of that name. Null when none.
    /// </summary>
    internal static DateTimeOffset? LastChangeOfName(string name, IReadOnlyList<TaskLabel> labels)
    {
        DateTimeOffset? newest = null;
        for (int suffix = 1; ; suffix++)
        {
            Guid id = TaskLabel.IdFor(suffix == 1 ? name : name.Trim() + "#" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture));
            TaskLabel? held = labels.FirstOrDefault(label => label.Id == id);
            if (held is null)
            {
                return newest;
            }

            if (held.IsLive && TaskLabel.SameName(held.Name, name))
            {
                continue;
            }

            DateTimeOffset changed = held.DeletedAt ?? held.EditedAt;
            if (newest is null || changed > newest)
            {
                newest = changed;
            }
        }
    }

    /// <summary>
    /// The id for a new label named <paramref name="name"/>: its name-based id (<see cref="TaskLabel.IdFor"/>), unless a live label
    /// renamed since holds that id; then the next free one of the name's numbered ids (the name with "#2", "#3"... appended to it
    /// before hashing). Never a live label's id, so a stale use of an old name can't undo a rename; the same labels give the same id
    /// on every device. A tombstone's id may be given (that label comes back).
    /// </summary>
    internal static Guid FreeIdFor(string name, IReadOnlyList<TaskLabel> labels)
    {
        Guid id = TaskLabel.IdFor(name);
        for (int suffix = 2; labels.Any(label => label.Id == id && label.IsLive); suffix++)
        {
            id = TaskLabel.IdFor(name.Trim() + "#" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

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
