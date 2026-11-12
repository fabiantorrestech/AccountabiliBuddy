using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// Merges the shared task list (ADR 76) field by field. Where both lists hold a live copy of a task, each
/// <see cref="TaskField"/> takes the newer stamp's value, so a change to one part on one device and to another part on another
/// device both survive. The title and the note also look at where each version came from (<see cref="TaskFieldStamp.Bases"/>):
/// a version made after seeing the other wins whatever its time; when each was made without seeing the other, the newer one is
/// shown and the other is kept on the task as a <see cref="TaskConflict"/> ("Changed on both devices"). Everything else
/// (tombstones, a revive, local items, the 30-day rule, the 2-minute future clamp, the order) is <see cref="SharedListMerge"/>'s.
/// The result is the same whichever list comes first. The phone's twin gives the same result.
/// </summary>
public static class TaskFieldMerge
{
    /// <summary>How many earlier values a title or note stamp remembers.</summary>
    public const int MaxBases = 10;

    /// <returns>The merged list, tombstones included, ordered as <see cref="SharedListMerge.Merge{T}"/> orders it.</returns>
    public static List<SyncTaskItem> Merge(IEnumerable<SyncTaskItem> first, IEnumerable<SyncTaskItem> second, DateTimeOffset now, Guid? self = null)
    {
        DateTimeOffset limit = now + SharedListMerge.FutureLimit;
        Dictionary<Guid, SyncTaskItem> firstById = Folded(first.Select(item => Clamped(item, limit)), now, self);
        Dictionary<Guid, SyncTaskItem> secondById = Folded(second.Select(item => Clamped(item, limit)), now, self);

        foreach (Guid id in firstById.Keys.Intersect(secondById.Keys).ToList())
        {
            SyncTaskItem one = firstById[id];
            SyncTaskItem other = secondById[id];
            if (one.Task.IsLive && other.Task.IsLive)
            {
                SyncTaskItem merged = MergeItem(one, other);
                firstById[id] = merged;
                secondById[id] = merged;
            }
        }

        return SharedListMerge.Merge(firstById.Values, secondById.Values, now, self);
    }

    /// <summary>Two live copies of the same task merged field by field; the same whichever is <paramref name="one"/>.</summary>
    public static SyncTaskItem MergeItem(SyncTaskItem one, SyncTaskItem other)
    {
        if (one.Id != other.Id)
        {
            throw new ArgumentException("Only copies of the same task merge.", nameof(other));
        }

        TaskItem a = one.Task;
        TaskItem b = other.Task;
        TaskItem result = a with
        {
            CreatedAt = a.CreatedAt <= b.CreatedAt ? a.CreatedAt : b.CreatedAt,
            EditedAt = a.EditedAt >= b.EditedAt ? a.EditedAt : b.EditedAt,
            TouchedAt = a.TouchedAt >= b.TouchedAt ? a.TouchedAt : b.TouchedAt,
            Origin = OriginOf(a, b),
            AutoSavedFor = a.AutoSavedFor is DateOnly dayA && b.AutoSavedFor is DateOnly dayB ? (dayA <= dayB ? dayA : dayB) : a.AutoSavedFor ?? b.AutoSavedFor,
            Effort = null,
        };

        TaskFieldStamps stamps = TaskFieldStamps.None;
        var conflicts = new List<TaskConflict>((a.Conflicts ?? []).Concat(b.Conflicts ?? []));
        foreach (TaskField field in TaskFieldValues.All)
        {
            TaskFieldStamp stampA = one.Stamps.Of(field) ?? Unstamped(a);
            TaskFieldStamp stampB = other.Stamps.Of(field) ?? Unstamped(b);
            string valueA = TaskFieldValues.Canonical(a, field);
            string valueB = TaskFieldValues.Canonical(b, field);

            FieldPick pick = field switch
            {
                TaskField.Title or TaskField.Note => PickText(field, a, b, stampA, stampB, valueA, valueB),
                TaskField.Number => PickNumber(a, b, stampA, stampB, valueA, valueB),
                _ => PickNewer(stampA, stampB, valueA, valueB),
            };

            result = TaskFieldValues.With(result, pick.FromFirst ? a : b, field);
            stamps = stamps.With(field, pick.Stamp);
            if (pick.Conflict is not null)
            {
                conflicts.Add(pick.Conflict);
            }
        }

        result = result with
        {
            FormerNumbers = FormerNumbersOf(a, b, result.Number),
            Conflicts = Surviving(conflicts, result, stamps),
        };
        return new SyncTaskItem(result, stamps);
    }

    private sealed record FieldPick(bool FromFirst, TaskFieldStamp Stamp, TaskConflict? Conflict = null);

    private static FieldPick PickNewer(TaskFieldStamp stampA, TaskFieldStamp stampB, string valueA, string valueB)
    {
        bool firstWins = FirstIsNewer(stampA, stampB, valueA, valueB);
        return new FieldPick(firstWins, firstWins ? stampA : stampB);
    }

    private static FieldPick PickText(TaskField field, TaskItem a, TaskItem b, TaskFieldStamp stampA, TaskFieldStamp stampB, string valueA, string valueB)
    {
        bool firstWins = FirstIsNewer(stampA, stampB, valueA, valueB);
        if (string.Equals(valueA, valueB, StringComparison.Ordinal))
        {
            // The same value: either history leads to it, so the stamp keeps both.
            TaskFieldStamp newer = firstWins ? stampA : stampB;
            TaskFieldStamp older = firstWins ? stampB : stampA;
            return new FieldPick(firstWins, newer with { Bases = UnionBases(newer.Bases, older.Bases) });
        }

        string? textA = TaskFieldValues.Text(a, field);
        string? textB = TaskFieldValues.Text(b, field);
        bool firstSawSecond = Holds(stampA.Bases, TaskFieldValues.Hash(textB));
        bool secondSawFirst = Holds(stampB.Bases, TaskFieldValues.Hash(textA));
        if (firstSawSecond != secondSawFirst)
        {
            // One version was made from the other: it wins whatever the clocks say.
            return firstSawSecond ? new FieldPick(true, stampA) : new FieldPick(false, stampB);
        }

        if (firstSawSecond)
        {
            // Each holds the other (an edit undone on one device): the newer stamp decides, no conflict.
            return new FieldPick(firstWins, firstWins ? stampA : stampB);
        }

        TaskFieldStamp winnerStamp = firstWins ? stampA : stampB;
        TaskFieldStamp loserStamp = firstWins ? stampB : stampA;
        string? loserText = firstWins ? textB : textA;
        string fieldName = field == TaskField.Title ? TaskConflict.TitleField : TaskConflict.NoteField;
        var conflict = new TaskConflict(fieldName, loserText, loserStamp.By, loserStamp.At);

        // The shown value keeps its own stamp but also remembers what the other version was made from: the task now carries that
        // version as the conflict, so an older copy of it (a third device that synced in between) is already seen, and the user's
        // pick (TaskEdits.Resolve, which builds on these bases) beats it instead of raising the conflict again. The other version's
        // own value stays out, or the conflict would close at once.
        IReadOnlyList<string>? bases = WinnerFirstBases(winnerStamp.Bases, loserStamp.Bases, TaskFieldValues.Hash(loserText));
        return new FieldPick(firstWins, winnerStamp with { Bases = bases }, conflict);
    }

    // The winner's bases all stay; the loser's (but never the loser's own value) fill what is left of the ring, placed before the
    // winner's (oldest first) so a later edit drops them first. The winner is the same whichever copy came first, so this is too.
    private static IReadOnlyList<string>? WinnerFirstBases(IReadOnlyList<string>? winner, IReadOnlyList<string>? loser, string loserValue)
    {
        List<string> own = (winner ?? []).ToList();
        if (own.Count > MaxBases)
        {
            own = own[^MaxBases..];
        }

        int room = MaxBases - own.Count;
        List<string> borrowed = (loser ?? [])
            .Where(hash => hash != loserValue && !Holds(own, hash))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (borrowed.Count > room)
        {
            borrowed = borrowed[^room..];
        }

        List<string> all = [.. borrowed, .. own];
        return all.Count == 0 ? null : all;
    }

    // A final number beats a temporary one (the numbering device's word is final); otherwise the newer stamp.
    private static FieldPick PickNumber(TaskItem a, TaskItem b, TaskFieldStamp stampA, TaskFieldStamp stampB, string valueA, string valueB)
    {
        int rankA = NumberRank(a.Number);
        int rankB = NumberRank(b.Number);
        if (rankA != rankB)
        {
            return rankA > rankB ? new FieldPick(true, stampA) : new FieldPick(false, stampB);
        }

        return PickNewer(stampA, stampB, valueA, valueB);
    }

    private static int NumberRank(TaskNumber? number)
    {
        return number switch
        {
            null => 0,
            { Temporary: true } => 1,
            _ => 2,
        };
    }

    // The temporary numbers either copy had, plus a temporary number that lost to another, so "143b" still finds the task.
    private static IReadOnlyList<int>? FormerNumbersOf(TaskItem a, TaskItem b, TaskNumber? kept)
    {
        var former = new SortedSet<int>((a.FormerNumbers ?? []).Concat(b.FormerNumbers ?? []));
        foreach (TaskNumber? number in new[] { a.Number, b.Number })
        {
            if (number is { Temporary: true } && number.Value != kept?.Value)
            {
                former.Add(number.Value);
            }
        }

        if (kept is not null)
        {
            former.Remove(kept.Value);
        }

        return former.Count == 0 ? null : former.ToList();
    }

    // A conflict stays while its other version is neither the shown value nor one the shown value was made from (the user's pick
    // is an edit whose bases hold both versions). One per field and other version; the same list whichever copy came first.
    private static IReadOnlyList<TaskConflict>? Surviving(IEnumerable<TaskConflict> conflicts, TaskItem merged, TaskFieldStamps stamps)
    {
        List<TaskConflict> kept = conflicts
            .Where(conflict => StillOpen(conflict, merged, stamps))
            .GroupBy(conflict => (conflict.Field, Hash: TaskFieldValues.Hash(conflict.OtherValue)))
            .Select(group => group
                .OrderByDescending(conflict => conflict.OtherEditedAt.UtcTicks)
                .ThenByDescending(conflict => conflict.OtherDeviceId.ToString("D"), StringComparer.Ordinal)
                .First())
            .OrderBy(conflict => conflict.Field, StringComparer.Ordinal)
            .ThenBy(conflict => TaskFieldValues.Hash(conflict.OtherValue), StringComparer.Ordinal)
            .ToList();
        return kept.Count == 0 ? null : kept;
    }

    private static bool StillOpen(TaskConflict conflict, TaskItem merged, TaskFieldStamps stamps)
    {
        TaskField? field = conflict.Field switch
        {
            TaskConflict.TitleField => TaskField.Title,
            TaskConflict.NoteField => TaskField.Note,
            _ => null,
        };
        if (field is not TaskField known)
        {
            return false;
        }

        string otherHash = TaskFieldValues.Hash(conflict.OtherValue);
        string shownHash = TaskFieldValues.Hash(TaskFieldValues.Text(merged, known));
        return otherHash != shownHash && !Holds(stamps.Of(known)?.Bases, otherHash);
    }

    /// <summary>The newer stamp: later time, then the device id, then the value, so every device picks the same.</summary>
    private static bool FirstIsNewer(TaskFieldStamp stampA, TaskFieldStamp stampB, string valueA, string valueB)
    {
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

        return string.CompareOrdinal(valueA, valueB) >= 0;
    }

    private static bool Holds(IReadOnlyList<string>? bases, string hash)
    {
        return bases is not null && bases.Contains(hash, StringComparer.Ordinal);
    }

    private static IReadOnlyList<string>? UnionBases(IReadOnlyList<string>? first, IReadOnlyList<string>? second)
    {
        List<string> all = (first ?? []).Concat((second ?? []).Where(hash => !Holds(first, hash))).ToList();
        if (all.Count == 0)
        {
            return null;
        }

        return all.Count > MaxBases ? all[^MaxBases..] : all;
    }

    private static string? OriginOf(TaskItem a, TaskItem b)
    {
        if (a.Origin is null || b.Origin is null)
        {
            return a.Origin ?? b.Origin;
        }

        return string.CompareOrdinal(a.Origin, b.Origin) <= 0 ? a.Origin : b.Origin;
    }

    private static TaskFieldStamp Unstamped(TaskItem task)
    {
        return new TaskFieldStamp(task.CreatedAt, Guid.Empty);
    }

    private static Dictionary<Guid, SyncTaskItem> Folded(IEnumerable<SyncTaskItem> items, DateTimeOffset now, Guid? self)
    {
        var byId = new Dictionary<Guid, SyncTaskItem>();
        foreach (SyncTaskItem item in items)
        {
            if (!byId.TryGetValue(item.Id, out SyncTaskItem? held))
            {
                byId[item.Id] = item;
            }
            else if (held.Task.IsLive && item.Task.IsLive)
            {
                byId[item.Id] = MergeItem(held, item);
            }
            else
            {
                byId[item.Id] = SharedListMerge.Merge([held], [item], now, self).Single();
            }
        }

        return byId;
    }

    // A stamp more than 2 minutes past the receiver's now (a fast clock) counts as that far ahead, the shared lists' rule.
    private static SyncTaskItem Clamped(SyncTaskItem item, DateTimeOffset limit)
    {
        TaskItem task = item.Task;
        TaskItem clampedTask = task with
        {
            EditedAt = Min(task.EditedAt, limit),
            TouchedAt = Min(task.TouchedAt, limit),
            DeletedAt = task.DeletedAt is DateTimeOffset deletedAt ? Min(deletedAt, limit) : null,
        };

        TaskFieldStamps stamps = item.Stamps;
        foreach (TaskField field in TaskFieldValues.All)
        {
            if (stamps.Of(field) is TaskFieldStamp stamp && stamp.At > limit)
            {
                stamps = stamps.With(field, stamp with { At = limit });
            }
        }

        return new SyncTaskItem(clampedTask, stamps);
    }

    private static DateTimeOffset Min(DateTimeOffset value, DateTimeOffset limit)
    {
        return value > limit ? limit : value;
    }
}
