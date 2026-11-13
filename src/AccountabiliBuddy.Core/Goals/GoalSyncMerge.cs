using System.Text.Json;
using System.Text.Json.Nodes;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// Merges two devices' goal lists (ADR 79). Pure: the same result whichever list comes first, and merging the result again with either
/// input changes nothing. The phone's twin gives the same result (parity fixtures).
/// <list type="bullet">
/// <item>Goals merge part by part (<see cref="GoalField"/>, owner 2026-11-14): where both hold a live copy, each part takes the newer
/// stamp's value (time, then device id, then value), silently. Tombstones, a revive by a later edit, the order, 30-day tombstones and the
/// 2-minute future clamp are <see cref="SharedListMerge"/>'s.</item>
/// <item>Areas, entries and reminders: whole item, newest wins. Entry tombstones are kept for good (GS1b): the goal history files keep
/// them for good, so a stale month, year or sync file can never bring a removed entry back, whatever order files arrive in. Reviews: one record per week (<see cref="GoalReviewIds"/>), old records
/// of the same week fold into it, each goal's decision the newest by its own time (<see cref="GoalReviewDecision.DecidedAt"/>, else
/// its record's edit time, written in by the fold), decisions ordered by goal id.</item>
/// <item>The first run's "Work" and Deep work, untouched (<see cref="GoalSeeds"/>), never win over an edited copy, whatever their
/// stamps: a live "Work" equal to the seed is dropped when another copy of it is edited; a Deep work part that still holds the seed's
/// value and was never changed on a device counts as older than any other. Deep work's created time is the earlier one (every goal's
/// is).</item>
/// <item>Deep work is never deleted, paused, re-kinded or given other sources by a merge: its tombstones are ignored and its record is
/// kept whole (<see cref="GoalSeeds.DeepWork"/>'s kind, target and source, no pauses). No other goal is built in.</item>
/// <item>Deleting a goal leaves its entries and reminders (<see cref="GoalTombstones.Delete"/>): they are kept, hidden while the goal
/// isn't live (<see cref="IsShown(GoalEntry, IReadOnlySet{Guid})"/>), and dropped once the goal is gone from the lists (its tombstone
/// pruned).</item>
/// <item>A tombstone carries no text and no unknown fields (<see cref="GoalTombstones.Clean(Goal)"/>). A waiting change
/// (<see cref="GoalTarget.PendingChange"/>) never takes part: it is left out of both inputs.</item>
/// <item>ADR 79 forward compatibility: unknown fields survive. Where the winning copy has none (an object without an unknown-field
/// list: its writer didn't know them), the other copy's are kept; where it has some, its own list wins whole (a newer writer that
/// dropped a field wins). Items of a newer app (<see cref="GoalRawItem"/>) take part by id and stamps, whole: a newer app's live copy
/// of a goal (Deep work included) that is newer than the known copy wins whole, part stamps unread; a newer app's Deep work tombstone
/// is left out like any Deep work tombstone.</item>
/// <item>Both inputs are cleaned first (<see cref="GoalSyncClean"/>): an item the merge can't use never throws, it is left out.</item>
/// <item>Times: the same instant written with two offsets is decided by the smaller offset (stamps, created and edit times), so the
/// result's text never depends on the order (the phone's twin compares the same way).</item>
/// <item>A live goal whose area isn't live goes to "Work" (<see cref="GoalAreaTidy"/>), stamped from the lists, never the clock; its
/// area part is stamped the same time by <see cref="GoalAreaTidy.StampId"/>, so the move holds on the next merge. The first run's
/// untouched "Work" holding no live goal loses to a "Work" tombstone (Q592).</item>
/// </list>
/// </summary>
public static class GoalSyncMerge
{
    /// <returns>The merged lists, tombstones included, each ordered by position and then id, newer items carried at their places.</returns>
    public static GoalSyncLists Merge(GoalSyncLists firstGiven, GoalSyncLists secondGiven, DateTimeOffset now)
    {
        return MergeReported(firstGiven, secondGiven, now).Lists;
    }

    /// <returns>
    /// <see cref="Merge"/>'s lists, and the entries it dropped because neither input holds their goal (live or tombstone): the merged copy of
    /// each, ordered by id. A goal history file that brought such a row is read again once the goal has arrived
    /// (<see cref="GoalHistoryRules.Merge"/>).
    /// </returns>
    public static GoalSyncMergeResult MergeReported(GoalSyncLists firstGiven, GoalSyncLists secondGiven, DateTimeOffset now)
    {
        DateTimeOffset limit = now + SharedListMerge.FutureLimit;
        GoalSyncLists first = GoalSyncClean.Lists(firstGiven);
        GoalSyncLists second = GoalSyncClean.Lists(secondGiven);

        (List<ISharedItem> areas, List<JsonElement> areasKept) = MergeAreas(first.Areas, second.Areas, now, limit);
        (List<ISharedItem> goals, List<JsonElement> goalsKept) = MergeGoals(first.Goals, second.Goals, now, limit);
        areas = EmptySeedWorkDeleted(areas, goals, first.Areas.Concat(second.Areas), now, limit);
        (areas, goals) = Rehomed(areas, goals);

        var goalIds = goals.Select(item => item.Id).ToHashSet();
        (List<ISharedItem> entries, List<JsonElement> entriesKept) = MergeWhole<GoalEntry>(first.Entries, second.Entries, now, limit,
            GoalTombstones.Clean, GoalUnknowns.Keep, keepTombstones: true);
        (List<ISharedItem> reminders, List<JsonElement> remindersKept) = MergeWhole<GoalReminder>(first.Reminders, second.Reminders, now, limit,
            GoalTombstones.Clean, GoalUnknowns.Keep);
        List<GoalEntry> dropped = entries.OfType<GoalEntry>().Where(entry => !goalIds.Contains(entry.GoalId))
            .OrderBy(entry => entry.Id.ToString("D"), StringComparer.Ordinal).ToList();
        entries = entries.Where(item => item is not GoalEntry entry || goalIds.Contains(entry.GoalId)).ToList();
        reminders = reminders.Where(item => item is not GoalReminder reminder || goalIds.Contains(reminder.GoalId)).ToList();
        (List<ISharedItem> reviews, List<JsonElement> reviewsKept) = MergeReviews(first.Reviews, second.Reviews, now, limit);

        var lists = new GoalSyncLists(
            Split<GoalArea>(areas, areasKept),
            Split<Goal>(goals, goalsKept),
            Split<GoalEntry>(entries, entriesKept),
            Split<GoalReminder>(reminders, remindersKept),
            Split<GoalReviewRecord>(reviews, reviewsKept));
        return new GoalSyncMergeResult(lists, dropped);
    }

    /// <summary>Two live copies of the same goal merged part by part; the same whichever is <paramref name="one"/>.</summary>
    public static Goal MergeGoal(Goal one, Goal other)
    {
        if (one.Id != other.Id)
        {
            throw new ArgumentException("Only copies of the same goal merge.", nameof(other));
        }

        GoalFieldStamps stampsA = GoalFieldStamps.For(one);
        GoalFieldStamps stampsB = GoalFieldStamps.For(other);
        Dictionary<GoalField, string> valuesA = GoalFieldValues.All(one);
        Dictionary<GoalField, string> valuesB = GoalFieldValues.All(other);
        Goal result = one with
        {
            CreatedAt = Earlier(one.CreatedAt, other.CreatedAt),
            EditedAt = Later(one.EditedAt, other.EditedAt),
            BuiltIn = one.BuiltIn || other.BuiltIn,
            Extra = ExtraOf(one, other),
        };

        GoalFieldStamps stamps = GoalFieldStamps.None;
        foreach (GoalField field in GoalFieldStamps.Fields)
        {
            GoalFieldStamp stampA = stampsA.Of(field)!;
            GoalFieldStamp stampB = stampsB.Of(field)!;
            bool firstWins = FirstIsNewer(
                GoalSeeds.IsPristine(one, field, stampA), stampA, valuesA[field],
                GoalSeeds.IsPristine(other, field, stampB), stampB, valuesB[field]);
            result = GoalFieldValues.With(result, firstWins ? one : other, field);
            stamps = stamps.With(field, firstWins ? stampA : stampB);
        }

        // Unknown stamps of a newer app's parts: the newer copy's list when it has one, else the other's.
        GoalUnknownFields? stampsExtra = ExtraOf(one.Stamps?.Extra, other.Stamps?.Extra, one, other);
        result = result with { Stamps = stamps with { Extra = stampsExtra } };

        // Nested unknown fields (target, rules, pauses, tiles) and a goal's newer rules: kept from the other copy where the winning
        // part has none. Each part came from one of the two, so only the other can fill it: the order of these two calls doesn't matter.
        return GoalUnknowns.Keep(other, GoalUnknowns.Keep(one, result));
    }

    /// <summary>Whether an entry is shown: its goal is live (a deleted goal's entries are kept, hidden, ADR 79).</summary>
    public static bool IsShown(GoalEntry entry, IReadOnlySet<Guid> liveGoalIds)
    {
        return entry.IsLive && liveGoalIds.Contains(entry.GoalId);
    }

    /// <summary>Whether a reminder is shown and may ring: its goal is live.</summary>
    public static bool IsShown(GoalReminder reminder, IReadOnlySet<Guid> liveGoalIds)
    {
        return reminder.IsLive && liveGoalIds.Contains(reminder.GoalId);
    }

    /// <summary>A goal without its waiting changes: they are this device's state and never take part in a merge or a file.</summary>
    public static Goal WithoutPending(Goal goal)
    {
        Goal result = goal;
        if (goal.Target is { PendingChange: not null } target)
        {
            result = result with { Target = target with { PendingChange = null } };
        }

        if (goal.SecondTarget is { PendingChange: not null } second)
        {
            result = result with { SecondTarget = second with { PendingChange = null } };
        }

        return result;
    }

    // ---------------------------------------------------------------- goals

    private static (List<ISharedItem> Merged, List<JsonElement> Kept) MergeGoals(IReadOnlyList<Goal> first, IReadOnlyList<Goal> second, DateTimeOffset now, DateTimeOffset limit)
    {
        Dictionary<Guid, Goal> firstById = FoldedGoals(first.Where(IsNotDeepWorkTombstone).Select(goal => Prepared(goal, limit)), now);
        Dictionary<Guid, Goal> secondById = FoldedGoals(second.Where(IsNotDeepWorkTombstone).Select(goal => Prepared(goal, limit)), now);
        foreach (Guid id in firstById.Keys.Intersect(secondById.Keys).ToList())
        {
            Goal one = firstById[id];
            Goal other = secondById[id];
            if (one.IsLive && other.IsLive)
            {
                Goal merged = MergeGoal(one, other);
                firstById[id] = merged;
                secondById[id] = merged;
            }
        }

        (List<ISharedItem> rawFirst, List<JsonElement> keptFirst) = Raw(first);
        (List<ISharedItem> rawSecond, List<JsonElement> keptSecond) = Raw(second);
        List<ISharedItem> winners = SharedListMerge.Merge(
            firstById.Values.Cast<ISharedItem>().Concat(rawFirst), secondById.Values.Cast<ISharedItem>().Concat(rawSecond), now);
        List<ISharedItem> kept = winners.Select(item => item is Goal goal ? Whole(goal) : item).ToList();
        return (kept, Passthrough(keptFirst, keptSecond));
    }

    private static bool IsNotDeepWorkTombstone(Goal goal)
    {
        return goal.IsLive || goal.Id != Goal.DeepWorkId;
    }

    // As a goal takes part: no waiting change, a tombstone cleaned, a live one with every part stamped, no stamp past the limit.
    private static Goal Prepared(Goal goal, DateTimeOffset limit)
    {
        Goal prepared = GoalTombstones.Clean(WithoutPending(goal));
        if (prepared.IsLive)
        {
            GoalFieldStamps stamps = GoalFieldStamps.For(prepared);
            foreach (GoalField field in GoalFieldStamps.Fields)
            {
                if (stamps.Of(field) is { } stamp && stamp.At > limit)
                {
                    stamps = stamps.With(field, stamp with { At = limit });
                }
            }

            prepared = prepared with { Stamps = stamps };
        }

        return prepared with
        {
            EditedAt = Min(prepared.EditedAt, limit),
            DeletedAt = prepared.DeletedAt is DateTimeOffset deletedAt ? Min(deletedAt, limit) : null,
        };
    }

    // Deep work kept whole whatever a copy said (its kind, placeholder target, its one source, never paused, built in); no other goal built in.
    private static Goal Whole(Goal goal)
    {
        if (!goal.IsLive)
        {
            return goal.BuiltIn ? goal with { BuiltIn = false } : goal;
        }

        if (goal.Id == Goal.DeepWorkId)
        {
            Goal seed = GoalSeeds.DeepWork(goal.CreatedAt);
            return goal with { Kind = seed.Kind, Target = seed.Target, Sources = seed.Sources, Pauses = null, BuiltIn = true };
        }

        return goal.BuiltIn ? goal with { BuiltIn = false } : goal;
    }

    private static Dictionary<Guid, Goal> FoldedGoals(IEnumerable<Goal> goals, DateTimeOffset now)
    {
        var byId = new Dictionary<Guid, Goal>();
        foreach (Goal goal in goals)
        {
            if (!byId.TryGetValue(goal.Id, out Goal? held))
            {
                byId[goal.Id] = goal;
            }
            else if (held.IsLive && goal.IsLive)
            {
                byId[goal.Id] = MergeGoal(held, goal);
            }
            else
            {
                byId[goal.Id] = SharedListMerge.Merge([held], [goal], now, keepTombstones: true).Single();
            }
        }

        return byId;
    }

    // The newer part: an untouched seed value loses to any other, then the later time, then the device id, then the value, then (the
    // same instant written with another offset) the smaller offset, so the result's text is the same whichever copy came first.
    private static bool FirstIsNewer(bool pristineA, GoalFieldStamp stampA, string valueA, bool pristineB, GoalFieldStamp stampB, string valueB)
    {
        if (pristineA != pristineB)
        {
            return pristineB;
        }

        int byTime = stampA.At.UtcTicks.CompareTo(stampB.At.UtcTicks);
        if (byTime != 0)
        {
            return byTime > 0;
        }

        int byDevice = string.CompareOrdinal(stampA.By.ToString("D"), stampB.By.ToString("D"));
        if (byDevice != 0)
        {
            return byDevice > 0;
        }

        int byValue = string.CompareOrdinal(valueA, valueB);
        if (byValue != 0)
        {
            return byValue > 0;
        }

        return stampA.At.Offset <= stampB.At.Offset;
    }

    // The earlier instant; the same instant with two offsets: the smaller offset (ADR 79: the text never depends on the order).
    private static DateTimeOffset Earlier(DateTimeOffset a, DateTimeOffset b)
    {
        int byTime = a.UtcTicks.CompareTo(b.UtcTicks);
        return byTime < 0 || (byTime == 0 && a.Offset <= b.Offset) ? a : b;
    }

    // The later instant; the same instant with two offsets: the smaller offset.
    private static DateTimeOffset Later(DateTimeOffset a, DateTimeOffset b)
    {
        int byTime = a.UtcTicks.CompareTo(b.UtcTicks);
        return byTime > 0 || (byTime == 0 && a.Offset <= b.Offset) ? a : b;
    }

    // A goal's own unknown fields: the newer copy's list when it has one (a newer writer that dropped a field wins), else the other's.
    private static GoalUnknownFields? ExtraOf(Goal one, Goal other)
    {
        return ExtraOf(one.Extra, other.Extra, one, other);
    }

    private static GoalUnknownFields? ExtraOf(GoalUnknownFields? extraA, GoalUnknownFields? extraB, Goal one, Goal other)
    {
        if (extraA is null || extraB is null)
        {
            return extraA ?? extraB;
        }

        int byTime = one.EditedAt.UtcTicks.CompareTo(other.EditedAt.UtcTicks);
        if (byTime != 0)
        {
            return byTime > 0 ? extraA : extraB;
        }

        return string.CompareOrdinal(ExtraText(extraA), ExtraText(extraB)) >= 0 ? extraA : extraB;
    }

    private static string ExtraText(GoalUnknownFields extra)
    {
        return string.Join("\n", extra.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + pair.Value.GetRawText()));
    }

    // ---------------------------------------------------------------- areas and the tidy

    private static (List<ISharedItem> Merged, List<JsonElement> Kept) MergeAreas(IReadOnlyList<GoalArea> first, IReadOnlyList<GoalArea> second, DateTimeOffset now, DateTimeOffset limit)
    {
        // The first run's untouched "Work" never wins over an edited live copy of it, whatever its stamp.
        bool workEdited = first.Concat(second).Any(area => area.Id == GoalSeeds.WorkAreaId && area.IsLive && !GoalSeeds.IsPristine(area));
        IReadOnlyList<GoalArea> Kept(IReadOnlyList<GoalArea> areas) => workEdited
            ? GoalNewerJson.WithNewer(areas.Where(area => !GoalSeeds.IsPristine(area)).ToList(), GoalNewerJson.NewerOf(areas))
            : areas;

        return MergeWhole<GoalArea>(Kept(first), Kept(second), now, limit, GoalTombstones.Clean, GoalUnknowns.Keep);
    }

    // Q592: the first run's untouched "Work" that holds no live goal after the goal merge loses to a "Work" tombstone of either input
    // (within the tombstones' 30 days), whatever the stamps: a device installed after the deletion never brings an empty "Work" back. A
    // goal moved into it later (the tidy) makes it again from the tombstone as before, so the result merges back to itself.
    private static List<ISharedItem> EmptySeedWorkDeleted(List<ISharedItem> areas, List<ISharedItem> goals, IEnumerable<GoalArea> inputs, DateTimeOffset now, DateTimeOffset limit)
    {
        int index = areas.FindIndex(item => item is GoalArea area && area.Id == GoalSeeds.WorkAreaId);
        if (index < 0 || areas[index] is not GoalArea work || !GoalSeeds.IsPristine(work)
            || goals.Any(item => item.DeletedAt is null && AreaOf(item) == GoalSeeds.WorkAreaId))
        {
            return areas;
        }

        DateTimeOffset keptFrom = now - SharedListMerge.TombstonesKept;
        GoalArea? tombstone = inputs
            .Where(area => area.Id == GoalSeeds.WorkAreaId && !area.IsLive)
            .Select(area => Clamped(GoalTombstones.Clean(area), limit))
            .Where(area => area.DeletedAt > keptFrom)
            .OrderByDescending(area => StampOf(area).UtcTicks)
            .ThenByDescending(area => area.TieBreak, StringComparer.Ordinal)
            .FirstOrDefault();
        if (tombstone is null)
        {
            return areas;
        }

        var replaced = new List<ISharedItem>(areas);
        replaced[index] = tombstone;
        return Ordered(replaced);
    }

    // The area a goal (or a newer app's goal) is in; null when it can't be read.
    private static Guid? AreaOf(ISharedItem item)
    {
        return item switch
        {
            Goal goal => goal.AreaId,
            GoalRawItem raw when raw.Json.TryGetProperty("areaId", out JsonElement value) && value.ValueKind == JsonValueKind.String
                && value.TryGetGuid(out Guid areaId) => areaId,
            _ => null,
        };
    }

    // A live goal in an area that isn't live goes to "Work" (GoalAreaTidy, the phone's rule too), its area part stamped as the move
    // by GoalAreaTidy.StampId (never the empty id: a Deep work moved back would read as the untouched seed and creep on every merge).
    private static (List<ISharedItem> Areas, List<ISharedItem> Goals) Rehomed(List<ISharedItem> areas, List<ISharedItem> goals)
    {
        List<GoalArea> knownAreas = areas.OfType<GoalArea>().ToList();

        // A newer app's live area still holds its goals: a stand-in tells the tidy it is live.
        List<GoalArea> standIns = areas.OfType<GoalRawItem>().Where(raw => raw.DeletedAt is null)
            .Select(raw => new GoalArea(raw.Id, string.Empty, string.Empty, string.Empty, raw.Position, raw.EditedAt)).ToList();
        List<Goal> knownGoals = goals.OfType<Goal>().ToList();
        (IReadOnlyList<GoalArea> tidiedAreas, IReadOnlyList<Goal> tidiedGoals) = GoalAreaTidy.Rehome(
            knownAreas.Concat(standIns).ToList(), knownGoals, GoalSeeds.WorkArea(DateTimeOffset.MinValue));
        if (ReferenceEquals(tidiedGoals, knownGoals) || tidiedGoals.SequenceEqual(knownGoals, ReferenceEqualityComparer.Instance))
        {
            return (areas, goals);
        }

        var movedGoals = new List<ISharedItem>(goals.Count);
        int index = 0;
        foreach (ISharedItem item in goals)
        {
            if (item is Goal)
            {
                Goal before = knownGoals[index];
                Goal after = tidiedGoals[index];
                index++;
                movedGoals.Add(after.AreaId == before.AreaId
                    ? after
                    : after with { Stamps = GoalFieldStamps.For(after).With(GoalField.Area, new GoalFieldStamp(after.EditedAt, GoalAreaTidy.StampId)) });
            }
            else
            {
                movedGoals.Add(item);
            }
        }

        var standInSet = new HashSet<GoalArea>(standIns, ReferenceEqualityComparer.Instance);
        List<ISharedItem> newAreas = tidiedAreas.Where(area => !standInSet.Contains(area)).Cast<ISharedItem>()
            .Concat(areas.OfType<GoalRawItem>())
            .ToList();
        return (Ordered(newAreas), movedGoals);
    }

    // ---------------------------------------------------------------- whole items

    private static (List<ISharedItem> Merged, List<JsonElement> Kept) MergeWhole<T>(
        IReadOnlyList<T> first, IReadOnlyList<T> second, DateTimeOffset now, DateTimeOffset limit, Func<T, T> clean, Func<T, T, T> keep,
        bool keepTombstones = false)
        where T : class, ISharedItem
    {
        List<T> firstKnown = first.Select(item => Clamped(clean(item), limit)).ToList();
        List<T> secondKnown = second.Select(item => Clamped(clean(item), limit)).ToList();
        (List<ISharedItem> rawFirst, List<JsonElement> keptFirst) = Raw(first);
        (List<ISharedItem> rawSecond, List<JsonElement> keptSecond) = Raw(second);
        List<ISharedItem> merged = SharedListMerge.Merge(firstKnown.Cast<ISharedItem>().Concat(rawFirst), secondKnown.Cast<ISharedItem>().Concat(rawSecond), now,
            keepTombstones: keepTombstones);

        // Missing unknown fields are kept from another copy of the same item (newest first) only where the winner has none at all.
        ILookup<Guid, T> copies = firstKnown.Concat(secondKnown).ToLookup(item => item.Id);
        List<ISharedItem> kept = merged.Select(item =>
        {
            if (item is not T winner || winner.DeletedAt is not null)
            {
                return item;
            }

            T result = winner;
            foreach (T copy in copies[winner.Id].Where(copy => !ReferenceEquals(copy, winner))
                .OrderByDescending(copy => StampOf(copy).UtcTicks).ThenByDescending(copy => copy.TieBreak, StringComparer.Ordinal))
            {
                result = keep(copy, result);
            }

            return (ISharedItem)result;
        }).ToList();
        return (kept, Passthrough(keptFirst, keptSecond));
    }

    private static T Clamped<T>(T item, DateTimeOffset limit)
        where T : ISharedItem
    {
        if (item.EditedAt <= limit && (item.DeletedAt is not DateTimeOffset deleted || deleted <= limit))
        {
            return item;
        }

        return (T)item.WithStamps(Min(item.EditedAt, limit), item.DeletedAt is DateTimeOffset deletedAt ? Min(deletedAt, limit) : null);
    }

    // ---------------------------------------------------------------- reviews

    private static (List<ISharedItem> Merged, List<JsonElement> Kept) MergeReviews(
        IReadOnlyList<GoalReviewRecord> first, IReadOnlyList<GoalReviewRecord> second, DateTimeOffset now, DateTimeOffset limit)
    {
        List<GoalReviewRecord> all = first.Concat(second)
            .Select(review => Clamped(GoalTombstones.Clean(review), limit))
            .Select(review => review.Id == GoalReviewIds.For(review.Week) ? review : review with { Id = GoalReviewIds.For(review.Week) })
            .ToList();

        var perWeek = new List<ISharedItem>();
        foreach (IGrouping<DateOnly, GoalReviewRecord> week in all.GroupBy(review => review.Week).OrderBy(group => group.Key))
        {
            var candidates = new List<ISharedItem>();
            List<GoalReviewRecord> live = week.Where(review => review.IsLive).ToList();
            if (live.Count > 0)
            {
                candidates.Add(Folded(live, limit));
            }

            if (week.Where(review => !review.IsLive).OrderByDescending(review => StampOf(review).UtcTicks)
                    .ThenByDescending(review => review.TieBreak, StringComparer.Ordinal).FirstOrDefault() is { } tombstone)
            {
                candidates.Add(tombstone);
            }

            perWeek.AddRange(SharedListMerge.Merge(candidates, [], now));
        }

        (List<ISharedItem> rawFirst, List<JsonElement> keptFirst) = Raw(first);
        (List<ISharedItem> rawSecond, List<JsonElement> keptSecond) = Raw(second);
        List<ISharedItem> merged = SharedListMerge.Merge(perWeek.Concat(rawFirst), rawSecond, now);
        return (merged, Passthrough(keptFirst, keptSecond));
    }

    // Live records of one week as one: the base fields (place, snooze, notes, unknown fields) of the newest; each goal's decision the
    // newest by its own time (decidedAt, else its record's edit time; clamped like any stamp; its JSON breaking a tie), written with
    // that time so it keeps it in every later fold (then the order records meet never matters, 3 devices included); decisions by goal
    // id, then ones without a readable goal id by their text.
    private static GoalReviewRecord Folded(List<GoalReviewRecord> live, DateTimeOffset limit)
    {
        List<GoalReviewRecord> newestFirst = live
            .OrderByDescending(review => review.EditedAt.UtcTicks)
            .ThenByDescending(BaseKey, StringComparer.Ordinal)
            .ThenByDescending(review => review.TieBreak, StringComparer.Ordinal)
            .ToList();
        GoalReviewRecord newest = newestFirst[0];

        // Every decision, known or kept as written, with its goal id (a kept one without a readable goal id stays as it is).
        var best = new Dictionary<string, (DateTimeOffset At, string Json, object Decision)>(StringComparer.Ordinal);
        var unkeyed = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (GoalReviewRecord review in newestFirst)
        {
            foreach (object written in DecisionsInOrder(review.Decisions ?? []))
            {
                string? key = DecisionKey(written);
                if (key is null)
                {
                    unkeyed.TryAdd(DecisionJson(written), (JsonElement)written);
                    continue;
                }

                (DateTimeOffset at, object decision) = Timed(written, review.EditedAt, limit);
                string json = DecisionJson(decision);
                if (!best.TryGetValue(key, out var held) || at.UtcTicks > held.At.UtcTicks
                    || (at.UtcTicks == held.At.UtcTicks && string.CompareOrdinal(json, held.Json) > 0))
                {
                    best[key] = (at, json, decision);
                }
            }
        }

        var known = new List<GoalReviewDecision>();
        var newer = new List<GoalNewerItem>();
        int index = 0;
        IEnumerable<object> ordered = best.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value.Decision)
            .Concat(unkeyed.Values.Cast<object>());
        foreach (object decision in ordered)
        {
            if (decision is GoalReviewDecision knownDecision)
            {
                known.Add(knownDecision);
            }
            else
            {
                newer.Add(new GoalNewerItem(index, (JsonElement)decision));
            }

            index++;
        }

        GoalUnknownFields? extra = newestFirst.Select(review => review.Extra).FirstOrDefault(fields => fields is not null);
        return newest with
        {
            Id = GoalReviewIds.For(newest.Week),
            Decisions = GoalNewerJson.WithNewer(known, newer),
            Extra = newest.Extra ?? extra,
        };
    }

    // A decision with its own time: its decidedAt (a newer app's kept decision: its "decidedAt" when readable), else its record's edit
    // time; past the future limit, the limit. Returned with that time written in.
    private static (DateTimeOffset At, object Decision) Timed(object decision, DateTimeOffset recordAt, DateTimeOffset limit)
    {
        if (decision is GoalReviewDecision known)
        {
            DateTimeOffset at = Min(known.DecidedAt ?? recordAt, limit);
            return (at, known.DecidedAt is DateTimeOffset own && own.EqualsExact(at) ? known : known with { DecidedAt = at });
        }

        var json = (JsonElement)decision;
        DateTimeOffset? written = json.TryGetProperty("decidedAt", out JsonElement value) && value.ValueKind == JsonValueKind.String
            && value.TryGetDateTimeOffset(out DateTimeOffset readable) ? readable : null;
        DateTimeOffset time = Min(written ?? recordAt, limit);
        if (written is DateTimeOffset same && same.EqualsExact(time))
        {
            return (time, json);
        }

        JsonObject node = JsonNode.Parse(json.GetRawText())!.AsObject();
        node["decidedAt"] = JsonSerializer.SerializeToNode(time, SyncJson.Options);
        using JsonDocument document = JsonDocument.Parse(node.ToJsonString(SyncJson.Options));
        return (time, document.RootElement.Clone());
    }

    // The fields a folded record takes from its newest record, as one text: equal texts, the same fields.
    private static string BaseKey(GoalReviewRecord review)
    {
        return JsonSerializer.Serialize(review with { Id = Guid.Empty, Decisions = [], EditedAt = default }, SyncJson.Options);
    }

    // A review's decisions, known ones and ones kept as written, in their written order.
    private static IEnumerable<object> DecisionsInOrder(IReadOnlyList<GoalReviewDecision> decisions)
    {
        List<GoalNewerItem> newer = GoalNewerJson.NewerOf(decisions).OrderBy(item => item.Index).ToList();
        int nextNewer = 0;
        int written = 0;
        foreach (GoalReviewDecision decision in decisions)
        {
            while (nextNewer < newer.Count && newer[nextNewer].Index <= written)
            {
                yield return newer[nextNewer++].Json;
                written++;
            }

            yield return decision;
            written++;
        }

        while (nextNewer < newer.Count)
        {
            yield return newer[nextNewer++].Json;
        }
    }

    private static string? DecisionKey(object decision)
    {
        if (decision is GoalReviewDecision known)
        {
            return known.GoalId.ToString("D");
        }

        JsonElement json = (JsonElement)decision;
        return json.ValueKind == JsonValueKind.Object && json.TryGetProperty("goalId", out JsonElement id) && id.ValueKind == JsonValueKind.String
            && id.TryGetGuid(out Guid goalId) ? goalId.ToString("D") : null;
    }

    private static string DecisionJson(object decision)
    {
        return decision is GoalReviewDecision known ? JsonSerializer.Serialize(known, SyncJson.Options) : ((JsonElement)decision).GetRawText();
    }

    // ---------------------------------------------------------------- shared helpers

    // A list's newer items: the ones whose id and stamps read take part; the others are carried along as written.
    private static (List<ISharedItem> Raw, List<JsonElement> Kept) Raw<T>(IReadOnlyList<T> list)
    {
        var raw = new List<ISharedItem>();
        var kept = new List<JsonElement>();
        foreach (GoalNewerItem item in GoalNewerJson.NewerOf(list))
        {
            if (GoalRawItem.TryRead(item.Json) is { } readable)
            {
                raw.Add(readable);
            }
            else
            {
                kept.Add(item.Json);
            }
        }

        return (raw, kept);
    }

    // Items carried along as written from both lists, once each, in text order (the same whichever list came first).
    private static List<JsonElement> Passthrough(List<JsonElement> first, List<JsonElement> second)
    {
        return first.Concat(second)
            .GroupBy(json => json.GetRawText(), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
    }

    // The known items as the list, the others carried at their places after them in the merged order.
    private static IReadOnlyList<T> Split<T>(List<ISharedItem> merged, List<JsonElement> kept)
    {
        var known = new List<T>();
        var newer = new List<GoalNewerItem>();
        int index = 0;
        foreach (ISharedItem item in merged)
        {
            if (item is T value)
            {
                known.Add(value);
            }
            else if (item is GoalRawItem raw)
            {
                newer.Add(new GoalNewerItem(index, raw.Json));
            }

            index++;
        }

        foreach (JsonElement json in kept)
        {
            newer.Add(new GoalNewerItem(index++, json));
        }

        return GoalNewerJson.WithNewer(known, newer);
    }

    private static List<ISharedItem> Ordered(List<ISharedItem> items)
    {
        return items.OrderBy(item => item.Position).ThenBy(item => item.Id.ToString("D"), StringComparer.Ordinal).ToList();
    }

    private static DateTimeOffset StampOf(ISharedItem item)
    {
        return item.DeletedAt is DateTimeOffset deletedAt && deletedAt > item.EditedAt ? deletedAt : item.EditedAt;
    }

    private static DateTimeOffset Min(DateTimeOffset value, DateTimeOffset limit)
    {
        return value > limit ? limit : value;
    }
}
