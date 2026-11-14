using AccountabiliBuddy.App.Services.Tasks;
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

    internal TypedNameScopes.NameState LabelNameState(string name) => _store is null ? TypedNameScopes.NameState.New : TypedNameScopes.LabelNameState(_store, name);

    // A typed name that is a label only other devices show: this device joins it and says so (the rules are in TypedNameScopes).
    private async Task JoinHiddenLabelAsync(string name)
    {
        if (_store is { IsActive: true } store && await TypedNameScopes.JoinHiddenLabelAsync(store, this, name) is string note)
        {
            Notice = note;
        }
    }

    // A label just typed in the name box or the add box: "Only on this device" applies to a NEW name only.
    private async Task ScopeNewLabelAsync(Guid? categoryId, bool onlyHere, TypedNameScopes.NameState before)
    {
        if (_store is { IsActive: true } store && LabelIdOf(categoryId) is Guid labelId && await TypedNameScopes.ScopeTypedLabelAsync(store, this, labelId, onlyHere, before) is string note)
        {
            Notice = note;
        }
    }

    // A tag typed in the name box or the add box: "Only on this device" applies to a NEW tag only; a hidden one is joined.
    private async Task ScopeNewTagAsync(string tag, bool onlyHere)
    {
        if (_store is { IsActive: true } store && await TypedNameScopes.ScopeTypedTagAsync(store, this, tag, onlyHere) is string note)
        {
            Notice = note;
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
