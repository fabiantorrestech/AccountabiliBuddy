namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// The merge and the resolver of the device roles (ADR 90). Pure; the phone's twin is Kotlin <c>SyncDeviceRoles</c>, and both give the
/// same bytes (fixtures <c>dr1/*</c>).
/// <list type="bullet">
/// <item><b>Merge:</b> one entry per id; the newest <see cref="SyncDeviceRole.At"/> wins, a tie goes to the higher <c>by</c> (the ordinal text of
/// its D form), then to the higher role word (ordinal). A time more than <see cref="SharedListMerge.FutureLimit"/> past the receiver's trusted
/// now is <b>ignored</b> when it arrives (the rule <c>GuidanceRules.Receive</c> follows, which filters only the received value): a fast clock
/// can't win every change, and nothing is stored capped, so a stored stamp never moves forward on a later merge. It counts once the
/// receiver's own time catches up. An entry this device already holds is never dropped, so a clock that steps back loses nothing. A missing list or
/// entry never deletes anything; an entry for a removed device (ADR 54) is left out, on the way in as well as on the way out; an entry with
/// no time or no writer, or whose role word isn't 1 to <see cref="MaxRoleWordLength"/> letters and hyphens, is left out. Symmetric and
/// idempotent.</item>
/// <item><b>Resolve:</b> at most one main phone, by construction.</item>
/// </list>
/// </summary>
public static class SyncDeviceRoles
{
    /// <summary>The most entries a device keeps (a file's size; a group never has this many Android devices).</summary>
    public const int MaxEntries = 64;

    /// <summary>The longest role word kept: the part carries ids, words and times, never text.</summary>
    public const int MaxRoleWordLength = 32;

    /// <summary>
    /// The merged list in id order (the ordinal text of the D form), or null when nothing is left (the part is then not written). An entry with
    /// no id or no role word is left out. <paramref name="removed"/> are the removed devices' ids (ADR 54); their entries are dropped. Only an
    /// <paramref name="incoming"/> entry more than 2 minutes ahead of <paramref name="now"/> is ignored; <paramref name="held"/> is never filtered
    /// by time (so the result is not symmetric for such an entry, by design).
    /// </summary>
    public static List<SyncDeviceRole>? Merge(
        IEnumerable<SyncDeviceRole>? held, IEnumerable<SyncDeviceRole>? incoming, DateTimeOffset now, IReadOnlySet<Guid>? removed = null)
    {
        DateTimeOffset limit = now + SharedListMerge.FutureLimit;
        var winners = new Dictionary<Guid, SyncDeviceRole>();
        foreach (SyncDeviceRole entry in (held ?? []).Select(entry => (entry, far: false)).Concat((incoming ?? []).Select(entry => (entry, far: entry is not null && entry.At > limit)))
            .Where(item => !item.far).Select(item => item.entry))
        {
            if (!IsUsable(entry) || (removed?.Contains(entry.Id) ?? false))
            {
                continue;
            }

            if (!winners.TryGetValue(entry.Id, out SyncDeviceRole? kept) || Newer(entry, kept))
            {
                winners[entry.Id] = entry;
            }
        }

        if (winners.Count == 0)
        {
            return null;
        }

        return winners.Values
            .OrderBy(entry => entry.Id.ToString("D"), StringComparer.Ordinal)
            .Take(MaxEntries)
            .ToList();
    }

    /// <summary>
    /// The list after <paramref name="id"/> is given <paramref name="role"/> at <paramref name="at"/> by <paramref name="by"/>. Setting a main
    /// also writes every other main entry as <see cref="SyncDeviceRole.RoleOther"/> with the same time and writer, so the newest write leaves one
    /// main. The time is stamped as <see cref="SharedListMerge.NextStamp"/> does: <paramref name="at"/>, or one tick past the newest of the
    /// entries it replaces when that is later, so a change made right after a faster clock's edit still wins. Setting the previous roles again
    /// (a Cancel) is the same call. Not merged: the caller merges it with what it holds.
    /// </summary>
    public static List<SyncDeviceRole> With(IEnumerable<SyncDeviceRole>? held, Guid id, string role, DateTimeOffset at, Guid by)
    {
        List<SyncDeviceRole> entries = (held ?? []).ToList();
        DateTimeOffset stamp = at;
        foreach (SyncDeviceRole entry in entries.Where(entry => entry.Id == id || (role == SyncDeviceRole.RoleMain && entry.Role == SyncDeviceRole.RoleMain)))
        {
            DateTimeOffset after = entry.At.AddTicks(1);
            stamp = after > stamp ? after : stamp;
        }

        var result = new List<SyncDeviceRole>();
        foreach (SyncDeviceRole entry in entries)
        {
            if (entry.Id == id)
            {
                continue;
            }

            bool demoted = role == SyncDeviceRole.RoleMain && entry.Role == SyncDeviceRole.RoleMain;
            result.Add(demoted ? entry with { Role = SyncDeviceRole.RoleOther, At = stamp, By = by } : entry);
        }

        result.Add(new SyncDeviceRole(id, role, stamp, by));
        return result.OrderBy(entry => entry.Id.ToString("D"), StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// The list after a change is cancelled (ADR 90): every entry the change touched is written again with the role it had in
    /// <paramref name="previous"/> (the list before the change), or <see cref="SyncDeviceRole.RoleNone"/> for a device that had no entry, all
    /// with one time and writer, stamped as <see cref="With"/> stamps (past every entry they replace). A change of main touches two or more
    /// entries (the new main and the previous main it demoted, and a new main's own earlier role): a single <see cref="With"/> call would
    /// restore only one. Entries the change did not touch are copied. Not merged: the caller merges it with what it holds.
    /// </summary>
    public static List<SyncDeviceRole> Restore(IEnumerable<SyncDeviceRole>? held, IEnumerable<SyncDeviceRole>? previous, DateTimeOffset at, Guid by)
    {
        Dictionary<Guid, SyncDeviceRole> now = (held ?? []).GroupBy(entry => entry.Id).ToDictionary(group => group.Key, group => group.OrderByDescending(entry => entry.At).First());
        Dictionary<Guid, SyncDeviceRole> before = (previous ?? []).GroupBy(entry => entry.Id).ToDictionary(group => group.Key, group => group.OrderByDescending(entry => entry.At).First());
        string RoleOf(Dictionary<Guid, SyncDeviceRole> list, Guid id) => list.TryGetValue(id, out SyncDeviceRole? entry) ? entry.Role : SyncDeviceRole.RoleNone;

        List<Guid> touched = now.Keys.Concat(before.Keys).Distinct().Where(id => RoleOf(now, id) != RoleOf(before, id)).ToList();
        DateTimeOffset stamp = at;
        foreach (Guid id in touched.Where(now.ContainsKey))
        {
            DateTimeOffset after = now[id].At.AddTicks(1);
            stamp = after > stamp ? after : stamp;
        }

        var result = new List<SyncDeviceRole>();
        foreach (Guid id in now.Keys.Concat(before.Keys).Distinct())
        {
            if (touched.Contains(id))
            {
                result.Add(new SyncDeviceRole(id, RoleOf(before, id), stamp, by));
            }
            else if (now.TryGetValue(id, out SyncDeviceRole? entry))
            {
                result.Add(entry);
            }
        }

        return result.OrderBy(entry => entry.Id.ToString("D"), StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// What each of <paramref name="androidIds"/> (the active Android devices, kind <c>phone</c>) effectively is. The newest-stamped <c>main</c>
    /// entry of an active Android device is the winning main (a tie: the higher <c>by</c>, then the higher id, ordinal D text); every other
    /// <c>main</c> entry resolves as Other. A device with an entry takes its role (an unknown word is Other); one without is Other when a
    /// winning main exists, else Unassigned. A <see cref="SyncDeviceRole.RoleNone"/> entry counts as no entry. Entries of devices that aren't in <paramref name="androidIds"/> are ignored. Several entries for
    /// one id (an unmerged list) count as their newest.
    /// </summary>
    public static IReadOnlyDictionary<Guid, SyncDeviceRoleKind> Effective(IEnumerable<SyncDeviceRole>? entries, IEnumerable<Guid> androidIds)
    {
        HashSet<Guid> android = androidIds.ToHashSet();
        var byId = new Dictionary<Guid, SyncDeviceRole>();
        foreach (SyncDeviceRole entry in (entries ?? []).Where(entry => IsUsable(entry) && android.Contains(entry.Id)))
        {
            if (!byId.TryGetValue(entry.Id, out SyncDeviceRole? kept) || Newer(entry, kept))
            {
                byId[entry.Id] = entry;
            }
        }

        // A none entry is the newest word for its device and then means "no entry".
        foreach (Guid none in byId.Where(pair => pair.Value.Role == SyncDeviceRole.RoleNone).Select(pair => pair.Key).ToList())
        {
            byId.Remove(none);
        }

        SyncDeviceRole? winner = null;
        foreach (SyncDeviceRole entry in byId.Values.Where(entry => entry.Role == SyncDeviceRole.RoleMain))
        {
            if (winner is null || NewerMain(entry, winner))
            {
                winner = entry;
            }
        }

        var result = new Dictionary<Guid, SyncDeviceRoleKind>();
        foreach (Guid id in android)
        {
            if (!byId.TryGetValue(id, out SyncDeviceRole? entry))
            {
                result[id] = winner is null ? SyncDeviceRoleKind.Unassigned : SyncDeviceRoleKind.Other;
                continue;
            }

            result[id] = entry.Role switch
            {
                SyncDeviceRole.RoleMain => winner is not null && winner.Id == id ? SyncDeviceRoleKind.Main : SyncDeviceRoleKind.Other,
                SyncDeviceRole.RoleTablet => SyncDeviceRoleKind.Tablet,
                _ => SyncDeviceRoleKind.Other,
            };
        }

        return result;
    }

    /// <summary>
    /// The device that is "the phone" as far as the guards' inputs go (ADR 90): the winning main's id, or null when there is none. The PC's
    /// alarm, bedtime and wake estimate, and steps and sleep readings, follow it.
    /// </summary>
    public static Guid? EffectiveMain(IReadOnlyDictionary<Guid, SyncDeviceRoleKind> effective)
    {
        foreach ((Guid id, SyncDeviceRoleKind kind) in effective)
        {
            if (kind == SyncDeviceRoleKind.Main)
            {
                return id;
            }
        }

        return null;
    }

    /// <summary>
    /// The devices the PC's sleep inputs (alarm, bedtime, wake estimate, steps) and the steps and sleep readings come from (ADR 90): the main
    /// phone when there is one, else every Unassigned device. Tablets and other devices are never among them.
    /// </summary>
    public static IReadOnlySet<Guid> ThePhones(IReadOnlyDictionary<Guid, SyncDeviceRoleKind> effective)
    {
        if (EffectiveMain(effective) is Guid main)
        {
            return new HashSet<Guid> { main };
        }

        return effective.Where(pair => pair.Value == SyncDeviceRoleKind.Unassigned).Select(pair => pair.Key).ToHashSet();
    }

    /// <summary>
    /// Whether the set of devices that feed the PC's sleep inputs changed between <paramref name="before"/> (what this device applied) and
    /// <paramref name="after"/> (what the list and the active Android devices say now): a role edit, a received list, a removal (ADR 54) or a
    /// device that left. This, not <see cref="MainChanged"/>, decides the wait (ADR 90): with no main, marking an Unassigned phone a tablet moves
    /// the inputs without moving a main. A device that was not in <paramref name="before"/> and only joins the set is not a change (a new device
    /// is not a role edit); one that leaves it, or one that was there and enters it, is. A change between Tablet and Other with a main set, or a
    /// role on a device outside the set, is not one.
    /// </summary>
    public static bool PhonesChanged(IReadOnlyDictionary<Guid, SyncDeviceRoleKind> before, IReadOnlyDictionary<Guid, SyncDeviceRoleKind> after)
    {
        IReadOnlySet<Guid> was = ThePhones(before);
        IReadOnlySet<Guid> now = ThePhones(after);
        return was.Any(id => !now.Contains(id)) || now.Any(id => !was.Contains(id) && before.ContainsKey(id));
    }

    /// <summary>
    /// Whether the effective main differs between <paramref name="before"/> (what this device applied) and <paramref name="after"/> (what the
    /// list and the active Android devices say now): a role edit, a received list, a removal (ADR 54) or a device that left. Any change of the
    /// effective main, including to or from none, is a change of <see cref="ThePhones"/> as well; <see cref="PhonesChanged"/> is the one that
    /// decides the wait, because with no main a change of an Unassigned device moves the inputs too.
    /// </summary>
    public static bool MainChanged(IReadOnlyDictionary<Guid, SyncDeviceRoleKind> before, IReadOnlyDictionary<Guid, SyncDeviceRoleKind> after)
    {
        return EffectiveMain(before) != EffectiveMain(after);
    }

    // An entry a device could have written: an id, a time, a writer and a plain role word.
    private static bool IsUsable(SyncDeviceRole? entry)
    {
        return entry is not null && entry.Id != Guid.Empty && entry.By != Guid.Empty && entry.At != default && IsRoleWord(entry.Role);
    }

    private static bool IsRoleWord(string? word)
    {
        return !string.IsNullOrEmpty(word) && word.Length <= MaxRoleWordLength && word.All(letter => letter is (>= 'a' and <= 'z') or '-');
    }

    // One id's two versions: the later time, then the higher by (ordinal D text), then the higher role word (ordinal).
    private static bool Newer(SyncDeviceRole candidate, SyncDeviceRole held)
    {
        if (candidate.At != held.At)
        {
            return candidate.At > held.At;
        }

        int byOrder = string.CompareOrdinal(candidate.By.ToString("D"), held.By.ToString("D"));
        return byOrder != 0 ? byOrder > 0 : string.CompareOrdinal(candidate.Role, held.Role) > 0;
    }

    // Two main entries of different devices: the later time, then the higher by, then the higher id (ordinal D text).
    private static bool NewerMain(SyncDeviceRole candidate, SyncDeviceRole held)
    {
        if (candidate.At != held.At)
        {
            return candidate.At > held.At;
        }

        int byOrder = string.CompareOrdinal(candidate.By.ToString("D"), held.By.ToString("D"));
        return byOrder != 0 ? byOrder > 0 : string.CompareOrdinal(candidate.Id.ToString("D"), held.Id.ToString("D")) > 0;
    }
}
