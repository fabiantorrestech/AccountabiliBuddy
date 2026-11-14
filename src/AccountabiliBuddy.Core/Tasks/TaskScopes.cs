using System.Security.Cryptography;
using System.Text;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// Which labels and tags a device shows (ADR 89, device-only labels and tags). A label or tag is shared by default. A device-only one
/// still syncs (its name travels sealed, and the task keeps it wherever the task is edited) but is shown only on the devices in its
/// list (<see cref="TaskLabel.OnlyOn"/>; for a tag, <see cref="SyncTagScope.OnlyOn"/>). Rules, all pure, the same on the phone
/// (TaskScopes.kt):
/// <list type="bullet">
/// <item>A null or empty list is shared. A list names several devices when they made the same name: it is shown on each of them.</item>
/// <item>A removed device (ADR 54) drops out of every list at display time, nothing is written: a list whose devices are all removed
/// counts as shared. A device not seen yet stays in its list, hidden here.</item>
/// <item>A device with no id (<c>self</c> empty: it isn't in a group) shows everything.</item>
/// <item>Every writer of a task's tags or label keeps the hidden ones (<see cref="WithHiddenTags"/>, <see cref="WithHiddenLabel"/>):
/// a device can't delete what it can't see. A label chosen on this device replaces a hidden one; that is the user's act.</item>
/// </list>
/// </summary>
public static class TaskScopes
{
    // A fixed namespace for the tag-scope ids (an RFC 4122 version 5 style id), distinct from the labels'; the phone's twin uses the same bytes.
    private static readonly Guid TagNameSpace = new("9d2c7e41-5b8a-4f36-a0d9-3e61c7b2f854");

    /// <summary>The scope id for a tag name: as <see cref="TaskLabel.IdFor"/> but in the tags' namespace. "Work" and "work" are one tag.</summary>
    public static Guid TagIdFor(string name)
    {
        byte[] space = TagNameSpace.ToByteArray(bigEndian: true);
        byte[] text = Encoding.UTF8.GetBytes(name.Trim().ToLowerInvariant());
        byte[] input = new byte[space.Length + text.Length];
        space.CopyTo(input, 0);
        text.CopyTo(input, space.Length);

        byte[] id = SHA1.HashData(input)[..16];
        id[6] = (byte)((id[6] & 0x0F) | 0x50);
        id[8] = (byte)((id[8] & 0x3F) | 0x80);
        return new Guid(id, bigEndian: true);
    }

    /// <summary>A device list in its stored form: no empty id, each once, in id order; none left is null (shared).</summary>
    public static IReadOnlyList<Guid>? Normalized(IReadOnlyList<Guid>? devices)
    {
        IReadOnlyList<Guid>? list = TaskRingDevices.Normalized(devices);
        return list is { Count: > 0 } ? list : null;
    }

    /// <summary>The devices that still show an item: the list without the removed devices; null when shared (no list, or every device in it removed).</summary>
    public static IReadOnlyList<Guid>? Effective(IReadOnlyList<Guid>? onlyOn, IReadOnlyCollection<Guid>? removed)
    {
        IReadOnlyList<Guid>? list = Normalized(onlyOn);
        if (list is null)
        {
            return null;
        }

        List<Guid> left = removed is null ? list.ToList() : list.Where(device => !removed.Contains(device)).ToList();
        return left.Count == 0 ? null : left;
    }

    /// <summary>Whether the list counts as shared: none, or every device in it removed.</summary>
    public static bool IsShared(IReadOnlyList<Guid>? onlyOn, IReadOnlyCollection<Guid>? removed)
    {
        return Effective(onlyOn, removed) is null;
    }

    /// <summary>Whether <paramref name="self"/> shows an item with this list. A device with no id shows everything.</summary>
    public static bool IsVisible(IReadOnlyList<Guid>? onlyOn, Guid self, IReadOnlyCollection<Guid>? removed)
    {
        if (self == Guid.Empty)
        {
            return true;
        }

        IReadOnlyList<Guid>? effective = Effective(onlyOn, removed);
        return effective is null || effective.Contains(self);
    }

    /// <summary>Whether the label is shown on <paramref name="self"/>.</summary>
    public static bool LabelVisible(TaskLabel label, Guid self, IReadOnlyCollection<Guid>? removed)
    {
        return IsVisible(label.OnlyOn, self, removed);
    }

    /// <summary>The live labels shown on <paramref name="self"/>, in the order given.</summary>
    public static IReadOnlyList<TaskLabel> VisibleLabels(IEnumerable<TaskLabel> labels, Guid self, IReadOnlyCollection<Guid>? removed)
    {
        return labels.Where(label => label.IsLive && LabelVisible(label, self, removed)).ToList();
    }

    /// <summary>The live scope of a tag; null when the tag has none (it is shared).</summary>
    public static SyncTagScope? ScopeOf(string tag, IEnumerable<SyncTagScope>? scopes)
    {
        Guid id = TagIdFor(tag);
        return scopes?.FirstOrDefault(scope => scope.Id == id && scope.DeletedAt is null);
    }

    /// <summary>Whether the tag is shown on <paramref name="self"/> (a tag with no live scope is shared).</summary>
    public static bool TagVisible(string tag, IEnumerable<SyncTagScope>? scopes, Guid self, IReadOnlyCollection<Guid>? removed)
    {
        return ScopeOf(tag, scopes) is not { } scope || IsVisible(scope.OnlyOn, self, removed);
    }

    /// <summary>A search's label test (<see cref="TaskSearch.Find"/>): true for the labels shown on <paramref name="self"/>.</summary>
    public static Func<TaskLabel, bool> LabelTest(Guid self, IReadOnlyCollection<Guid>? removed)
    {
        return label => LabelVisible(label, self, removed);
    }

    /// <summary>A search's tag test (<see cref="TaskSearch.Find"/>): true for the tags shown on <paramref name="self"/>.</summary>
    public static Func<string, bool> TagTest(IReadOnlyList<SyncTagScope>? scopes, Guid self, IReadOnlyCollection<Guid>? removed)
    {
        return tag => TagVisible(tag, scopes, self, removed);
    }

    /// <summary>The label a task shows: its label when shown on <paramref name="self"/>, else null ("No label": the label itself is kept).</summary>
    public static Guid? ShownLabel(Guid? labelId, IEnumerable<TaskLabel> labels, Guid self, IReadOnlyCollection<Guid>? removed)
    {
        return labelId is Guid id && labels.FirstOrDefault(label => label.Id == id && label.IsLive) is { } held && LabelVisible(held, self, removed) ? id : null;
    }

    /// <summary>The tags a task shows: the ones shown on <paramref name="self"/>; null when none.</summary>
    public static IReadOnlyList<string>? ShownTags(IReadOnlyList<string>? tags, IReadOnlyList<SyncTagScope>? scopes, Guid self, IReadOnlyCollection<Guid>? removed)
    {
        IReadOnlyList<string>? clean = TaskLabels.Clean(tags);
        return TaskLabels.Clean(clean?.Where(tag => TagVisible(tag, scopes, self, removed)));
    }

    /// <summary>
    /// The tags to store after an edit of the shown ones: <paramref name="shownEdit"/> (what the user left of the tags this device
    /// shows, or typed) plus every tag <paramref name="held"/> has that this device doesn't show. Every writer of a task's tags goes
    /// through this. A tag the user typed that is hidden here counts as theirs (they chose it; the caller makes it theirs with
    /// <see cref="TagScopeEdits.SetScope"/>). Hidden tags always stay: when the list is full, the shown ones give way.
    /// </summary>
    public static IReadOnlyList<string>? WithHiddenTags(
        IReadOnlyList<string>? shownEdit, IReadOnlyList<string>? held, IReadOnlyList<SyncTagScope>? scopes, Guid self, IReadOnlyCollection<Guid>? removed)
    {
        IReadOnlyList<string> edit = TaskLabels.Clean(shownEdit) ?? [];
        List<string> hidden = (TaskLabels.Clean(held) ?? [])
            .Where(tag => !TagVisible(tag, scopes, self, removed) && !TaskLabels.Has(edit, tag))
            .ToList();
        IEnumerable<string> shown = edit.Take(Math.Max(0, TaskLabels.MaxPerTask - hidden.Count));
        return TaskLabels.Clean(shown.Concat(hidden));
    }

    /// <summary>
    /// The label to store after an edit of the label this device shows: <paramref name="chosen"/> when the user picked one (it replaces a
    /// hidden one: their act); else the label <paramref name="held"/> when it is hidden here (a task shown as "No label" keeps its
    /// device-only label); else none.
    /// </summary>
    public static Guid? WithHiddenLabel(Guid? chosen, Guid? held, IEnumerable<TaskLabel> labels, Guid self, IReadOnlyCollection<Guid>? removed)
    {
        if (chosen is not null)
        {
            return chosen;
        }

        return held is Guid id && labels.FirstOrDefault(label => label.Id == id && label.IsLive) is { } label && !LabelVisible(label, self, removed) ? id : null;
    }

    /// <summary>
    /// A file's tag scopes cleaned: an entry with no id, a live one with no name or no devices, or a list over
    /// <see cref="TaskRingDevices.MaxDevices"/> devices is left out (never a deletion: the merge keeps what it has); device lists in
    /// their stored form; a tombstone holds neither name nor devices. Null stays null.
    /// </summary>
    public static IReadOnlyList<SyncTagScope>? Clean(IReadOnlyList<SyncTagScope?>? scopes)
    {
        if (scopes is null)
        {
            return null;
        }

        var clean = new List<SyncTagScope>();
        foreach (SyncTagScope? scope in scopes)
        {
            if (scope is null || scope.Id == Guid.Empty)
            {
                continue;
            }

            if (scope.DeletedAt is not null)
            {
                clean.Add(scope with { Name = null, OnlyOn = null });
                continue;
            }

            IReadOnlyList<Guid>? devices = Normalized(scope.OnlyOn);
            if (string.IsNullOrWhiteSpace(scope.Name) || devices is null || devices.Count > TaskRingDevices.MaxDevices)
            {
                continue;
            }

            clean.Add(scope with { OnlyOn = devices });
        }

        return clean;
    }
}
