using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// Who writes and removes derived day values (ADR 79, orchestrator review item 2, plan check §1 items 9-10, §4 item 4). Pure: given this
/// device's id and its fresh readings, the entry changes to make. The phone's twin gives the same changes.
/// <list type="bullet">
/// <item>One entry per goal + source + device + day (<see cref="GoalDerivedIds"/>), marked with the device's id, written only by the
/// device that measured it; updated in place, and only when its value changed (its edit time then moves, <see cref="SharedListMerge.NextStamp"/>).</item>
/// <item>A real 0: Health Connect and screen time write a day measured as 0 (an "at most" goal can't tell 0 from no data). The #tag and
/// calendar extras write nothing for a 0 unless they had a value that day (then it becomes 0).</item>
/// <item>Inside a read's cover (<see cref="GoalDerivedCover"/>) this device's own entry for a day the read no longer has becomes a tombstone;
/// a reading for a day whose own entry is a tombstone brings it back.</item>
/// <item>Never touched: hand entries, entries written before devices were recorded (no device), another device's entries, and Beeminder's
/// (only a PC with a Beeminder token changes or removes those, <see cref="MayChange"/>; their own rules stay in the PC's Beeminder entry rules).</item>
/// </list>
/// </summary>
public static class GoalDerivedWriter
{
    /// <summary>The sources a device measures itself and writes under its own id: Health Connect, screen time, the #tag extra, calendar.</summary>
    public static bool IsDerived(GoalEntrySource source)
    {
        return source is GoalEntrySource.HealthConnect or GoalEntrySource.ScreenTime or GoalEntrySource.FocusLocal or GoalEntrySource.Calendar;
    }

    /// <summary>Whether a 0 is written as a real value (Health Connect, screen time); the #tag and calendar extras write a 0 only over an earlier value.</summary>
    public static bool WritesZero(GoalEntrySource source)
    {
        return source is GoalEntrySource.HealthConnect or GoalEntrySource.ScreenTime;
    }

    /// <summary>Whether <paramref name="entry"/> is <paramref name="deviceId"/>'s own derived value (its device and its deterministic id).</summary>
    public static bool IsOwn(GoalEntry entry, Guid deviceId)
    {
        return entry.DeviceId == deviceId && GoalDerivedIds.IsDerivedId(entry);
    }

    /// <summary>
    /// Whether <paramref name="deviceId"/> may change or remove <paramref name="entry"/> as a reading: its own derived value, or a Beeminder
    /// entry on a PC with a Beeminder token (one id per goal + slug + day, the same from every such PC). Never a hand entry.
    /// </summary>
    public static bool MayChange(GoalEntry entry, Guid deviceId, bool hasBeeminderToken)
    {
        return entry.Source == GoalEntrySource.Beeminder ? hasBeeminderToken : IsOwn(entry, deviceId);
    }

    /// <returns>
    /// The entries to put (new, updated in place, brought back, or made tombstones), ordered by id; empty when nothing changed. A later reading
    /// for the same goal, source and day replaces an earlier one in <paramref name="readings"/>.
    /// </returns>
    /// <exception cref="ArgumentException">A reading or cover of a source that isn't device-measured (<see cref="IsDerived"/>).</exception>
    public static IReadOnlyList<GoalEntry> Changes(
        IReadOnlyList<GoalEntry> held, Guid deviceId, IReadOnlyList<GoalDerivedReading> readings, IReadOnlyList<GoalDerivedCover> covered, DateTimeOffset now)
    {
        if (readings.FirstOrDefault(reading => !IsDerived(reading.Source)) is { } wrong)
        {
            throw new ArgumentException($"{wrong.Source} values are not written as a device's derived value.", nameof(readings));
        }

        if (covered.FirstOrDefault(cover => !IsDerived(cover.Source)) is { } wrongCover)
        {
            throw new ArgumentException($"{wrongCover.Source} values are not written as a device's derived value.", nameof(covered));
        }

        // This device's own entries by id (the newest copy when a list holds one twice).
        var own = new Dictionary<Guid, GoalEntry>();
        foreach (GoalEntry entry in held.Where(entry => entry is not null && IsOwn(entry, deviceId)))
        {
            if (!own.TryGetValue(entry.Id, out GoalEntry? kept) || entry.Stamp > kept.Stamp)
            {
                own[entry.Id] = entry;
            }
        }

        var fresh = new Dictionary<Guid, GoalDerivedReading>();
        foreach (GoalDerivedReading reading in readings)
        {
            fresh[GoalDerivedIds.For(reading.GoalId, reading.Source, deviceId, reading.Day)] = reading;
        }

        var changes = new Dictionary<Guid, GoalEntry>();
        foreach ((Guid id, GoalDerivedReading reading) in fresh)
        {
            own.TryGetValue(id, out GoalEntry? before);
            if (before is null || !before.IsLive)
            {
                if (reading.Value == 0 && !WritesZero(reading.Source))
                {
                    continue;
                }

                DateTimeOffset at = SharedListMerge.NextStamp(now, before);
                changes[id] = new GoalEntry(id, reading.GoalId, reading.Day, null, reading.Value, reading.Source, at, before?.Position ?? 0, null, deviceId);
            }
            else if (before.Value != reading.Value)
            {
                changes[id] = before with { Value = reading.Value, EditedAt = SharedListMerge.NextStamp(now, before) };
            }
        }

        foreach (GoalEntry entry in own.Values)
        {
            if (entry.IsLive && !fresh.ContainsKey(entry.Id)
                && covered.Any(cover => cover.GoalId == entry.GoalId && cover.Source == entry.Source && entry.Day >= cover.From && entry.Day <= cover.To))
            {
                DateTimeOffset at = SharedListMerge.NextStamp(now, entry);
                changes[entry.Id] = entry with { EditedAt = at, DeletedAt = at };
            }
        }

        return changes.Values.OrderBy(entry => entry.Id.ToString("D"), StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// <paramref name="held"/> with <paramref name="changes"/> put in: an entry with the same id replaced in place, a new one added at the end;
    /// the list's newer items (<see cref="GoalNewerJson"/>) kept.
    /// </summary>
    public static IReadOnlyList<GoalEntry> Applied(IReadOnlyList<GoalEntry> held, IReadOnlyList<GoalEntry> changes)
    {
        if (changes.Count == 0)
        {
            return held;
        }

        var byId = changes.ToDictionary(entry => entry.Id);
        var result = new List<GoalEntry>(held.Count + changes.Count);
        var placed = new HashSet<Guid>();
        foreach (GoalEntry entry in held)
        {
            if (entry is not null && byId.TryGetValue(entry.Id, out GoalEntry? changed))
            {
                if (placed.Add(entry.Id))
                {
                    result.Add(changed);
                }

                continue;
            }

            result.Add(entry!);
        }

        result.AddRange(changes.Where(entry => !placed.Contains(entry.Id)));
        return GoalNewerJson.WithNewer(result, GoalNewerJson.NewerOf(held));
    }
}
