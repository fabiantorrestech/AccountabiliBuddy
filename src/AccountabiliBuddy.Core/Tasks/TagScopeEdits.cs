using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// The edits of a tag's scope (ADR 89), each stamped so the newest version wins on every device (<see cref="SharedListMerge"/>). A tag
/// has a scope only while it is device-only; "make shared" deletes the scope, so a tag with no live scope is shared.
/// </summary>
public static class TagScopeEdits
{
    /// <summary>
    /// Makes <paramref name="tag"/> device-only on <paramref name="self"/> (<paramref name="onlyOnThisDevice"/>), or shared. A device-only
    /// tag some other device already made joins that device's list instead of replacing it (the same name on two devices is shared by
    /// them). A device with no id (<paramref name="self"/> empty) can't be named: nothing changes. The scopes come back unchanged when
    /// the tag already is as wanted.
    /// </summary>
    public static IReadOnlyList<SyncTagScope>? SetScope(IReadOnlyList<SyncTagScope>? scopes, string tag, bool onlyOnThisDevice, DateTimeOffset now, Guid self)
    {
        string name = tag.Trim();
        if (name.Length == 0 || (onlyOnThisDevice && self == Guid.Empty))
        {
            return scopes;
        }

        Guid id = TaskScopes.TagIdFor(name);
        List<SyncTagScope> list = (scopes ?? []).ToList();
        int index = list.FindIndex(scope => scope.Id == id);
        SyncTagScope? held = index >= 0 ? list[index] : null;
        bool live = held is { DeletedAt: null };
        DateTimeOffset stamp = SharedListMerge.NextStamp(now, held);

        SyncTagScope? changed;
        if (onlyOnThisDevice)
        {
            IReadOnlyList<Guid> devices = TaskScopes.Normalized(live ? held!.OnlyOn?.Append(self).ToList() : [self]) ?? [self];
            changed = live && TaskRingDevices.Same(held!.OnlyOn, devices) ? null : new SyncTagScope(id, live ? held!.Name : name, devices, stamp);
        }
        else
        {
            changed = live ? held!.Deleted(stamp) : null;
        }

        if (changed is null)
        {
            return scopes;
        }

        if (index >= 0)
        {
            list[index] = changed;
        }
        else
        {
            list.Add(changed);
        }

        return list.OrderBy(scope => scope.Id.ToString("D"), StringComparer.Ordinal).ToList();
    }
}
