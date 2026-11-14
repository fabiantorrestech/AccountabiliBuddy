using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// ADR 89: what typing a label or tag name in the name box, "New tag…" or the add box does to the scope of a name that may already exist.
/// "Only on this device" applies to a NEW name only: a name that is shared keeps its scope (the Labels screen switches it, with its
/// confirm), and a name only other devices show is JOINED (this device is added to its list, never replaces it). The methods return the
/// note to show the user (null for none). The phone's twins are in ShelfEdits / TaskLabelEdits (the same rules).
/// </summary>
public static class TypedNameScopes
{
    /// <summary>What a typed label name is on this device: not a label yet, a label this device shows, or one only other devices show.</summary>
    public enum NameState
    {
        New,
        Shown,
        Hidden,
    }

    /// <summary>The state of a typed label name (checked before the name is adopted).</summary>
    public static NameState LabelNameState(TaskStore store, string name)
    {
        TaskLabel? label = store.Lists.Labels.Select(item => item.Label).FirstOrDefault(item => item.IsLive && TaskLabel.SameName(item.Name, name.Trim()));
        return label is null ? NameState.New : store.LabelShown(label) ? NameState.Shown : NameState.Hidden;
    }

    /// <summary>
    /// A typed name that is a label only other devices show: this device joins it (a stamped edit of its device list that keeps the
    /// devices already in it). Done before the name is adopted, so the label is shown here by then. The note says which devices have it.
    /// </summary>
    public static async Task<string?> JoinHiddenLabelAsync(TaskStore store, object source, string name)
    {
        if (!store.IsActive)
        {
            return null;
        }

        string? alsoOn = null;
        await store.ChangeAsync(source, (lists, self, now) =>
        {
            TaskLabel? hidden = lists.Labels.Select(item => item.Label).FirstOrDefault(item => item.IsLive && TaskLabel.SameName(item.Name, name.Trim()) && !store.LabelShown(item));
            if (hidden is null || self == Guid.Empty)
            {
                return lists;
            }

            alsoOn = store.OtherDevicesText(hidden.OnlyOn);
            return TaskLabelEdits.SetOnlyOn(lists, hidden.Id, TaskLabelEdits.Joined(hidden.OnlyOn, self), now, self);
        });
        return alsoOn is null ? null : $"“{name.Trim()}” is also on {alsoOn}. This device joined it.";
    }

    /// <summary>
    /// A label just typed with "Only on this device" ticked: made device-only only when the name was new (<paramref name="before"/>); a label
    /// this device already showed keeps its scope (the note says where to change it); a hidden one was joined before.
    /// </summary>
    public static async Task<string?> ScopeTypedLabelAsync(TaskStore store, object source, Guid labelId, bool onlyHere, NameState before)
    {
        if (!onlyHere || !store.IsActive)
        {
            return null;
        }

        if (before == NameState.Shown)
        {
            TaskLabel? shown = store.Lists.Labels.Select(item => item.Label).FirstOrDefault(item => item.Id == labelId && item.IsLive);
            return $"“{shown?.Name}” already exists" + (shown is not null && store.IsShared(shown) ? " and is shared. Change that on the Labels screen." : ".");
        }

        if (before == NameState.New)
        {
            await store.ChangeAsync(source, (lists, self, now) => self == Guid.Empty ? lists : TaskLabelEdits.SetOnlyOn(lists, labelId, TaskLabelEdits.Joined(null, self), now, self));
        }

        return null;
    }

    /// <summary>
    /// A tag typed with "Only on this device": made device-only only when the tag is new (no task has ever carried it); a hidden one is
    /// joined (the scope's devices stay); a tag that exists and is shared keeps its scope, with a note.
    /// </summary>
    public static async Task<string?> ScopeTypedTagAsync(TaskStore store, object source, string tag, bool onlyHere)
    {
        if (!onlyHere || !store.IsActive)
        {
            return null;
        }

        bool hidden = !store.TagShown(tag);
        if (!hidden && store.TagExists(tag))
        {
            return store.IsTagShared(tag) ? $"#{tag} already exists and is shared. Change that on the Labels screen." : null;
        }

        string? alsoOn = hidden ? store.OtherDevicesText(TaskScopes.ScopeOf(tag, store.Lists.TagScopes)?.OnlyOn) : null;
        await store.ChangeAsync(source, (lists, self, now) => self == Guid.Empty ? lists : lists with { TagScopes = TagScopeEdits.SetScope(lists.TagScopes, tag, true, now, self) });
        return alsoOn is null ? null : $"#{tag} is also on {alsoOn}. This device joined it.";
    }
}
