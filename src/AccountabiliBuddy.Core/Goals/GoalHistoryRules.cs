using System.Text.Json;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// The shared goal history's rules (ADR 79, owner 2026-11-14 "shared history"; GS1b). Pure, no transport (GS3, GS4 list, upload, download
/// and remove the files). The phone's twin gives the same results. Months and years are UTC, of an entry's stamp (edit time, or deletion
/// time when later).
/// <list type="bullet">
/// <item><b>Month files</b> (<see cref="GoalHistoryMonth"/>): a device writes its file for the current month and for the month before (late
/// changes and the closing write), when the content changed and at most once a day (<see cref="MonthToWrite"/>); the month before also gets
/// one closing write once it has ended. Older months never change.</item>
/// <item><b>Reading</b> (<see cref="ToRead"/>): files new or changed since this device last read them; every file when it is new to the group;
/// every file of a device (and every year file) whose last complete merge here is older than 90 days.</item>
/// <item><b>Year files</b> (<see cref="RollUp"/>): once a year has ended and January's late writes are in (from 1 February UTC), the active
/// PC with the lowest id (<see cref="Roller"/>) rolls every device's month files of that year into one year file; a later roll-up only adds.</item>
/// <item><b>Removing a month file</b> (<see cref="MayRemoveMonth"/>): only once the year file holds that version of it and every active
/// device's sync file says it merged that year file's version (<see cref="SyncFile.GoalYearsMerged"/>, <see cref="Marked"/>).</item>
/// <item><b>Merging</b> (<see cref="Merge"/>): the regular file's goal parts first, then the history rows, through <see cref="GoalSyncMerge"/>
/// (entries of goals the device doesn't hold are dropped, so history merges together with or after the goals part). Union by id, newest
/// wins, entry tombstones kept for good everywhere, so regular, month and year files converge in any order and a stale file never brings a
/// removed entry back.</item>
/// </list>
/// </summary>
public static class GoalHistoryRules
{
    /// <summary>A device rewrites its month file at most this often (the closing write excepted).</summary>
    public static readonly TimeSpan RewriteEvery = TimeSpan.FromDays(1);

    /// <summary>A device whose last complete merge here is older than this is read in full (its entries may have left its 90-day window).</summary>
    public static readonly TimeSpan FullReadAfter = GoalSyncWindow.Window;

    private static readonly JsonSerializerOptions RowsOptions = new(SyncJson.Options) { Converters = { new SyncGoalEntryListConverter() } };

    /// <summary>The UTC year and month of the entry's stamp: the month file it goes to.</summary>
    public static (int Year, int Month) MonthOf(GoalEntry entry)
    {
        DateTime stamp = entry.Stamp.UtcDateTime;
        return (stamp.Year, stamp.Month);
    }

    /// <summary>The months a device may write now: the current UTC month and the one before.</summary>
    public static IReadOnlyList<(int Year, int Month)> WritableMonths(DateTimeOffset now)
    {
        DateTime current = now.UtcDateTime;
        DateTime previous = new DateTime(current.Year, current.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-1);
        return [(previous.Year, previous.Month), (current.Year, current.Month)];
    }

    /// <summary>Whether the month may still be written (<see cref="WritableMonths"/>).</summary>
    public static bool IsWritable(int year, int month, DateTimeOffset now)
    {
        return WritableMonths(now).Contains((year, month));
    }

    /// <summary>
    /// What the month file holds: every entry of <paramref name="held"/> (tombstones and newer items whose stamp reads included) whose stamp
    /// is in that month, ordered by id (<c>D</c> text, ordinal), newer items after the known ones.
    /// </summary>
    public static IReadOnlyList<GoalEntry> MonthContent(IReadOnlyList<GoalEntry> held, int year, int month)
    {
        List<GoalEntry> known = held
            .Where(entry => entry is not null && MonthOf(entry) == (year, month))
            .OrderBy(entry => entry.Id.ToString("D"), StringComparer.Ordinal)
            .ToList();
        List<GoalNewerItem> newer = GoalNewerJson.NewerOf(held)
            .Select(item => (Item: item, Raw: GoalRawItem.TryRead(item.Json)))
            .Where(pair => pair.Raw is { } raw && StampMonth(raw) == (year, month))
            .OrderBy(pair => pair.Raw!.Id.ToString("D"), StringComparer.Ordinal)
            .Select((pair, order) => new GoalNewerItem(known.Count + order, pair.Item.Json))
            .ToList();
        return GoalNewerJson.WithNewer(known, newer);
    }

    /// <returns>
    /// The month file to write now, or null when there is nothing to write: the month isn't writable; its content is what
    /// <paramref name="last"/> (the version this device wrote before, null for none) holds; the last write was less than
    /// <see cref="RewriteEvery"/> ago, unless the month has ended since it (the closing write); or there is no file yet and nothing to put in.
    /// </returns>
    public static GoalHistoryMonth? MonthToWrite(GoalHistoryMonth? last, IReadOnlyList<GoalEntry> held, Guid deviceId, int year, int month, DateTimeOffset now)
    {
        if (!IsWritable(year, month, now))
        {
            return null;
        }

        IReadOnlyList<GoalEntry> content = MonthContent(held, year, month);
        if (last is null)
        {
            return content.Count == 0 && GoalNewerJson.NewerOf(content).Count == 0
                ? null
                : new GoalHistoryMonth(GoalHistoryMonth.CurrentFormatVersion, deviceId, year, month, now, content);
        }

        if (RowsText(content) == RowsText(last.Entries))
        {
            return null;
        }

        DateTimeOffset monthEnd = MonthStart(year, month).AddMonths(1);
        bool closing = now >= monthEnd && last.WrittenAt < monthEnd;
        if (!closing && now - last.WrittenAt < RewriteEvery)
        {
            return null;
        }

        DateTimeOffset writtenAt = now > last.WrittenAt ? now : last.WrittenAt.AddTicks(1);
        return new GoalHistoryMonth(GoalHistoryMonth.CurrentFormatVersion, deviceId, year, month, writtenAt, content);
    }

    /// <summary>
    /// The listed files to read now, year files first (by year), then month files (by year, month, device id text): every file when
    /// <paramref name="newToGroup"/>; else each file new or changed since <paramref name="lastRead"/> (this device's own month files
    /// skipped), every month file of a device whose last complete merge here (<paramref name="lastMergeFrom"/>: its regular file, or a full
    /// read of its files; GS3/GS4 set it after either) is missing or older than <see cref="FullReadAfter"/>, and then every year file too.
    /// </summary>
    public static IReadOnlyList<GoalHistoryListing> ToRead(
        IReadOnlyList<GoalHistoryListing> listing,
        IReadOnlyDictionary<GoalHistoryFileKey, DateTimeOffset> lastRead,
        IReadOnlyDictionary<Guid, DateTimeOffset> lastMergeFrom,
        Guid self,
        bool newToGroup,
        DateTimeOffset now)
    {
        bool Stale(Guid device)
        {
            return !lastMergeFrom.TryGetValue(device, out DateTimeOffset merged) || now - merged > FullReadAfter;
        }

        bool Changed(GoalHistoryListing file)
        {
            return !lastRead.TryGetValue(file.Key, out DateTimeOffset read) || read != file.ModifiedAt;
        }

        bool anyStale = listing
            .Where(file => file.Key.Kind == GoalHistoryFileKind.Month && file.Key.DeviceId is Guid device && device != self)
            .Select(file => file.Key.DeviceId!.Value)
            .Concat(lastMergeFrom.Keys.Where(device => device != self))
            .Distinct()
            .Any(Stale);

        return listing
            .Where(file => newToGroup || file.Key switch
            {
                { Kind: GoalHistoryFileKind.Year } => anyStale || Changed(file),
                { Kind: GoalHistoryFileKind.Month, DeviceId: Guid device } => device != self && (Stale(device) || Changed(file)),
                _ => false,
            })
            .OrderBy(file => file.Key.Kind == GoalHistoryFileKind.Year ? 0 : 1)
            .ThenBy(file => file.Key.Year)
            .ThenBy(file => file.Key.Month ?? 0)
            .ThenBy(file => file.Key.DeviceId?.ToString("D") ?? string.Empty, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The PC that rolls the year files: the active PC whose id (<c>D</c> text, ordinal) is the lowest; null with no PC.</summary>
    public static Guid? Roller(IEnumerable<GoalHistoryDevice> activeDevices)
    {
        return activeDevices
            .Where(device => device.IsPc)
            .OrderBy(device => device.Id.ToString("D"), StringComparer.Ordinal)
            .Select(device => (Guid?)device.Id)
            .FirstOrDefault();
    }

    /// <summary>Whether the year may be rolled: from 1 February UTC of the next year (January may still write December).</summary>
    public static bool RollDue(int year, DateTimeOffset now)
    {
        return now >= new DateTimeOffset(year + 1, 2, 1, 0, 0, 0, TimeSpan.Zero);
    }

    /// <returns>
    /// The year file to write: <paramref name="existing"/> (null for none) with every month file of <paramref name="year"/> in
    /// <paramref name="months"/> it doesn't hold yet (by device, month and version) added; null when the year isn't due or nothing is new.
    /// Only adds: every entry id and month of <paramref name="existing"/> stays.
    /// </returns>
    public static GoalHistoryYear? RollUp(GoalHistoryYear? existing, IEnumerable<GoalHistoryMonth> months, int year, Guid rolledBy, DateTimeOffset now)
    {
        if (!RollDue(year, now) || (existing is not null && existing.Year != year))
        {
            return null;
        }

        List<GoalHistoryMonth> ofYear = months.Where(month => month is not null && month.Year == year).ToList();
        IReadOnlyList<GoalHistoryPiece> held = existing?.Months ?? [];
        List<GoalHistoryMonth> fresh = ofYear
            .Where(month => !held.Any(piece => piece.DeviceId == month.DeviceId && piece.Month == month.Month && piece.WrittenAt >= month.WrittenAt))
            .ToList();
        if (fresh.Count == 0)
        {
            return null;
        }

        List<GoalHistoryPiece> pieces = held
            .Concat(fresh.Select(month => new GoalHistoryPiece(month.DeviceId, month.Month, month.WrittenAt)))
            .GroupBy(piece => (piece.DeviceId, piece.Month))
            .Select(group => group.OrderByDescending(piece => piece.WrittenAt.UtcTicks).First())
            .OrderBy(piece => piece.DeviceId.ToString("D"), StringComparer.Ordinal)
            .ThenBy(piece => piece.Month)
            .ToList();
        IReadOnlyList<GoalEntry> entries = MergeEntries(fresh.Select(month => month.Entries).Prepend(existing?.Entries ?? []), now);
        DateTimeOffset rolledAt = existing is null || now > existing.RolledAt ? now : existing.RolledAt.AddTicks(1);
        return new GoalHistoryYear(GoalHistoryYear.CurrentFormatVersion, year, rolledBy, rolledAt, pieces, entries);
    }

    /// <summary>
    /// Whether a month file (<paramref name="monthKey"/>, version <paramref name="monthWrittenAt"/>) may be removed: its month can no longer
    /// be written, the year file holds that version of it, and every active device has merged that year file's version (its sync file's
    /// <see cref="SyncFile.GoalYearsMerged"/>). Who removes it is the transport's (each device its own files on the relay).
    /// </summary>
    public static bool MayRemoveMonth(GoalHistoryFileKey monthKey, DateTimeOffset monthWrittenAt, GoalHistoryYear? year, IReadOnlyList<GoalHistoryDevice> activeDevices, DateTimeOffset now)
    {
        if (monthKey.Kind != GoalHistoryFileKind.Month || monthKey.DeviceId is not Guid device || monthKey.Month is not int month
            || year is null || year.Year != monthKey.Year || IsWritable(monthKey.Year, month, now) || activeDevices.Count == 0)
        {
            return false;
        }

        bool held = year.Months.Any(piece => piece.DeviceId == device && piece.Month == month && piece.WrittenAt >= monthWrittenAt);
        return held && activeDevices.All(active => active.YearsMerged?.Any(marker => marker.Year == year.Year && marker.RolledAt >= year.RolledAt) == true);
    }

    /// <summary>This device's "merged year files" marker after it merged <paramref name="merged"/>: that year's newest version, by year.</summary>
    public static IReadOnlyList<SyncGoalYearMerged> Marked(IReadOnlyList<SyncGoalYearMerged>? marker, GoalHistoryYear merged)
    {
        return (marker ?? [])
            .Where(item => item is not null)
            .Append(new SyncGoalYearMerged(merged.Year, merged.RolledAt))
            .GroupBy(item => item.Year)
            .Select(group => group.OrderByDescending(item => item.RolledAt.UtcTicks).First())
            .OrderBy(item => item.Year)
            .ToList();
    }

    /// <summary>
    /// History rows merged with each other: union by id, newest wins (<see cref="SharedListMerge"/>, the 2-minute future clamp), tombstones
    /// kept for good, newer items by id and stamps (unreadable ones carried once); ordered by id.
    /// </summary>
    public static IReadOnlyList<GoalEntry> MergeEntries(IEnumerable<IReadOnlyList<GoalEntry>> lists, DateTimeOffset now)
    {
        var items = new List<ISharedItem>();
        var carried = new List<JsonElement>();
        foreach (IReadOnlyList<GoalEntry> list in lists)
        {
            items.AddRange((GoalSyncClean.Entries(list) ?? []).Select(GoalTombstones.Clean));
            foreach (GoalNewerItem item in GoalNewerJson.NewerOf(list))
            {
                if (GoalRawItem.TryRead(item.Json) is { } raw)
                {
                    items.Add(raw);
                }
                else
                {
                    carried.Add(item.Json);
                }
            }
        }

        List<ISharedItem> merged = SharedListMerge.Merge(items, [], now, keepTombstones: true)
            .OrderBy(item => item.Id.ToString("D"), StringComparer.Ordinal)
            .ToList();
        List<GoalEntry> known = merged.OfType<GoalEntry>().ToList();
        List<GoalNewerItem> newer = merged.OfType<GoalRawItem>().Select(raw => raw.Json)
            .Concat(carried.GroupBy(json => json.GetRawText(), StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => group.First()))
            .Select((json, order) => new GoalNewerItem(known.Count + order, json))
            .ToList();
        return GoalNewerJson.WithNewer(known, newer);
    }

    /// <summary>
    /// <paramref name="held"/> after a regular file's goal parts (<paramref name="file"/>, null for none) and then the history files' rows: the
    /// goals part first, so the rows of a goal that file brings are kept (rows of a goal this device doesn't hold are dropped by the merge).
    /// </summary>
    public static GoalSyncLists Merge(GoalSyncLists held, GoalSyncLists? file, IEnumerable<IReadOnlyList<GoalEntry>> history, DateTimeOffset now)
    {
        GoalSyncLists result = file is null ? held : GoalSyncMerge.Merge(held, file, now);
        List<IReadOnlyList<GoalEntry>> lists = history.Where(list => list is not null).ToList();
        if (lists.Count == 0)
        {
            return result;
        }

        return GoalSyncMerge.Merge(result, GoalSyncLists.Empty with { Entries = MergeEntries(lists, now) }, now);
    }

    /// <summary>The rows' JSON text (<see cref="SyncGoalEntryListConverter"/>): what "the content changed" compares.</summary>
    public static string RowsText(IReadOnlyList<GoalEntry> entries)
    {
        return JsonSerializer.Serialize(entries, RowsOptions);
    }

    private static DateTimeOffset MonthStart(int year, int month)
    {
        return new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private static (int Year, int Month) StampMonth(GoalRawItem raw)
    {
        DateTime stamp = (raw.DeletedAt is DateTimeOffset deleted && deleted > raw.EditedAt ? deleted : raw.EditedAt).UtcDateTime;
        return (stamp.Year, stamp.Month);
    }
}
