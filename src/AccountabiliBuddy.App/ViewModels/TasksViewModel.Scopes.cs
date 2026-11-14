using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

// ADR 89 on the Tasks page: every label and tag picker lists "Shared" (on every device) first, then "This device" (device-only ones this
// device shows). Other devices' device-only labels and tags are never listed. Shared = Devices icon, This device = Laptop icon (the
// "Keep on…" menu's meanings); the phone uses its twins.
public sealed partial class TasksViewModel
{
    /// <summary>The headings: shown once this device is in a sync group (before that nothing can be device-only).</summary>
    internal bool GroupsByScope => _store is { IsActive: true, CanScope: true };

    /// <summary>The name box's "Only on this device" (ADR 89): off by default, so a new label or tag is shared.</summary>
    [ObservableProperty]
    public partial bool NewOnlyHere { get; set; }

    /// <summary>Whether the name box offers "Only on this device".</summary>
    public bool CanChooseScope => GroupsByScope;

    // A label just made from the name box: device-only when asked.
    private async Task ScopeNewLabelAsync(Guid? categoryId, bool onlyHere)
    {
        if (!onlyHere || _store is not { IsActive: true } store || LabelIdOf(categoryId) is not Guid labelId)
        {
            return;
        }

        await store.ChangeAsync(this, (lists, self, now) => self == Guid.Empty ? lists : TaskLabelEdits.SetOnlyOn(lists, labelId, TaskLabelEdits.Joined(null, self), now, self));
    }

    // A tag just made from the name box: device-only when asked.
    private async Task ScopeNewTagAsync(string tag, bool onlyHere)
    {
        if (onlyHere && _store is { IsActive: true } store)
        {
            await store.ChangeAsync(this, (lists, self, now) => lists with { TagScopes = TagScopeEdits.SetScope(lists.TagScopes, tag, true, now, self) });
        }
    }

    // The entries under "Shared", then under "This device" (only when it has some). Without a group: the entries as they are.
    internal List<TaskMenuEntry> ScopeGrouped(IReadOnlyList<(bool Shared, TaskMenuEntry Entry)> items)
    {
        return ScopeMenu.Group(GroupsByScope, items);
    }

    // Whether the category (the PC's old id for a label) is a shared label.
    private bool IsSharedCategory(Guid categoryId)
    {
        return LabelOf(LabelIdOf(categoryId)) is not { } label || _store is null || _store.IsShared(label);
    }

    private bool IsSharedTag(string tag)
    {
        return _store is null || _store.IsTagShared(tag);
    }

    /// <summary>The tags a task row shows: the ones this device shows.</summary>
    internal IReadOnlyList<string> ShownTagsOf(IReadOnlyList<string>? tags)
    {
        return _store is { IsActive: true } store ? store.ShownTags(tags) : tags ?? [];
    }
}
