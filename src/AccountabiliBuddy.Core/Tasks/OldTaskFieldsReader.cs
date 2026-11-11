using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// Reads another device's old task fields into the task list (ADR 76), for a file without <see cref="SyncFile.Tasks"/> (an app
/// older than ADR 76, today's phone). A file with <see cref="SyncFile.Tasks"/> is the truth for its device and its old fields are
/// ignored (<see cref="ReadFile"/>).
/// <list type="bullet">
/// <item>Item by item, by id. When an id is in several old lists, the newest live copy gives the fields, and a copy in the list of
/// the task's own bin decides the bin (so a task an older app shows in two lists doesn't move).</item>
/// <item>A field changes only when the old item's value differs from what this device would write for the task in that list, and
/// the old item's edit time is newer than that field's stamp; it is stamped with the old edit time and the sender. A text field
/// counts as edited from this device's value, so it never raises "Changed on both devices".</item>
/// <item>Nothing the old types can't carry is touched: reminders, highlight, due time, number, devices other than a keeper,
/// step focus and estimates (kept by step id), can't miss, calendar, origin, Next (an older app shows Next as Later and can't
/// move it between the two).</item>
/// <item>The bin follows the old list only for a real change: a tick, an untick (back to where it was) or a move to another old
/// list (to its first bin; This session only while the session has room).</item>
/// <item>An old tombstone newer than the task deletes it; a local item's stub (ADR 72) makes it kept on that device; a newer
/// old item brings back a deleted task, or a finished one from History with all it had. Unknown items become tasks.</item>
/// <item>Old categories become labels by name; a newer colour choice is taken (ADR 62).</item>
/// </list>
/// </summary>
public static class OldTaskFieldsReader
{
    private static readonly DateTimeOffset UnstampedAt = GuidanceState.UpgradeStamp;

    /// <summary>The file's old fields read in; null when the file carries <see cref="SyncFile.Tasks"/> (merge those instead).</summary>
    public static OldTaskReadResult? ReadFile(SyncFile file, OldTaskReadInput input)
    {
        return file.Tasks is not null ? null : Read(input with { Incoming = OldTaskFields.Of(file) });
    }

    public static OldTaskReadResult Read(OldTaskReadInput input)
    {
        var reading = new Reading(input);
        return reading.Run();
    }

    private sealed class Reading
    {
        private readonly OldTaskReadInput _input;
        private readonly DateTimeOffset _limit;
        private readonly List<SyncTaskItem> _tasks;
        private readonly List<SyncTaskLogItem> _history;
        private readonly TaskLabelBook _book;
        private int _fieldsApplied;
        private int _added;
        private int _deleted;
        private int _revived;
        private int _moved;

        public Reading(OldTaskReadInput input)
        {
            _input = input;
            _limit = input.Now + SharedListMerge.FutureLimit;
            _tasks = input.Tasks.ToList();
            _history = input.History.ToList();
            _book = new TaskLabelBook(input.Labels.Select(item => item.Label));
        }

        public OldTaskReadResult Run()
        {
            AdoptCategories();

            OldTaskFields incoming = _input.Incoming;
            IEnumerable<OldTaskShape> shapes = incoming.NextTasks.Select(OldTaskShape.Of)
                .Concat(incoming.Later.Select(OldTaskShape.Of))
                .Concat(incoming.Parked.Select(OldTaskShape.Of))
                .Concat(incoming.Comebacks.Select(OldTaskShape.Of))
                .Where(shape => shape.Id != Guid.Empty)
                .Select(Clamped);

            foreach (IGrouping<Guid, OldTaskShape> group in shapes.GroupBy(shape => shape.Id).OrderBy(group => group.Key.ToString("D"), StringComparer.Ordinal))
            {
                Handle(group.ToList());
            }

            return new OldTaskReadResult(
                _tasks,
                _book.Labels.Select(label => new SyncTaskLabel(label)).ToList(),
                _history,
                _fieldsApplied,
                _added,
                _deleted,
                _revived,
                _moved);
        }

        private void AdoptCategories()
        {
            foreach (SyncCategory category in _input.Incoming.Categories)
            {
                if (category.Name is null || category.Name.Trim().Length == 0)
                {
                    continue;
                }

                TaskLabel? label = _book.Named(category.Name);
                if (label is null)
                {
                    _book.IdForName(category.Name, category.Color, category.ColorEditedAt ?? UnstampedAt);
                    _fieldsApplied++;
                }
                else if (category.ColorEditedAt is DateTimeOffset chosenAt && chosenAt <= _limit && chosenAt > label.EditedAt
                    && !TaskLabelColors.SameOpaque(category.Color, label.Color))
                {
                    _book.Put(label with { Color = TaskLabelColors.Opaque(category.Color), EditedAt = chosenAt });
                    _fieldsApplied++;
                }
            }
        }

        private void Handle(List<OldTaskShape> copies)
        {
            Guid id = copies[0].Id;
            List<OldTaskShape> live = copies.Where(copy => copy.IsLive && !copy.IsLegacyComeback)
                .OrderByDescending(copy => copy.EditedAt.UtcTicks).ThenBy(copy => copy.List).ToList();
            OldTaskShape? newestDead = copies.Where(copy => !copy.IsLive)
                .OrderByDescending(copy => copy.DeletedAt!.Value.UtcTicks).FirstOrDefault();
            OldTaskShape? newestLive = live.FirstOrDefault();
            int taskIndex = _tasks.FindIndex(item => item.Id == id);
            int historyIndex = _history.FindIndex(item => item.Id == id && item.Entry.IsLive);

            if (newestDead is not null && (newestLive is null || newestDead.DeletedAt > newestLive.EditedAt))
            {
                HandleDeleted(newestDead, taskIndex);
                return;
            }

            if (newestLive is null)
            {
                // Only the legacy open come-backs list has it: it can only add a task nobody has.
                if (taskIndex < 0 && historyIndex < 0)
                {
                    _tasks.Add(FromOld(copies[0], null));
                    _added++;
                }

                return;
            }

            if (taskIndex >= 0 && _tasks[taskIndex].Task.IsLive)
            {
                TaskItem task = _tasks[taskIndex].Task;
                OldTaskShape binSource = live.FirstOrDefault(copy => copy.List == OldTaskFieldsWriter.ListOf(task.Bin)) ?? newestLive;
                Apply(taskIndex, newestLive, binSource);
                return;
            }

            if (taskIndex >= 0)
            {
                // A tombstone here: a newer edit there brings the task back, with what History kept of it.
                TaskItem tombstone = _tasks[taskIndex].Task;
                if (newestLive.EditedAt > (tombstone.DeletedAt ?? tombstone.EditedAt))
                {
                    _tasks[taskIndex] = FromOld(newestLive, historyIndex >= 0 ? _history[historyIndex].Entry.Task : null);
                    CloseHistory(historyIndex, newestLive.EditedAt);
                    _revived++;
                }

                return;
            }

            if (historyIndex >= 0)
            {
                TaskHistoryEntry entry = _history[historyIndex].Entry;
                TaskItem shown = entry.Task with { Bin = entry.Task.PrevBin ?? TaskBin.Later };
                OldTaskShape projected = OldTaskShape.Projected(shown, newestLive.List, _book.Labels);
                if (newestLive.EditedAt > entry.EditedAt && Differs(newestLive, projected))
                {
                    _tasks.Add(FromOld(newestLive, entry.Task));
                    CloseHistory(historyIndex, newestLive.EditedAt);
                    _revived++;
                }

                return;
            }

            _tasks.Add(FromOld(newestLive, null));
            _added++;
        }

        private void HandleDeleted(OldTaskShape dead, int taskIndex)
        {
            DateTimeOffset at = dead.DeletedAt!.Value;
            if (dead.IsStub && dead.LocalTo == _input.Self)
            {
                return;
            }

            IReadOnlyList<Guid>? devices = dead.IsStub ? [dead.LocalTo!.Value] : null;
            if (taskIndex < 0)
            {
                TaskBin bin = DefaultBin(dead.List, dead.BinMarker, countsInSession: false);
                var tombstone = new TaskItem(dead.Id, string.Empty, bin, 0, dead.CreatedAt ?? dead.EditedAt, at, at, Devices: devices, DeletedAt: at);
                _tasks.Add(new SyncTaskItem(tombstone, TaskFieldStamps.None));
                _added++;
                return;
            }

            SyncTaskItem held = _tasks[taskIndex];
            if (held.Task.IsLive && at > held.Task.EditedAt)
            {
                SyncTaskItem deleted = held.Deleted(at);
                _tasks[taskIndex] = devices is null ? deleted : deleted with { Task = deleted.Task with { Devices = devices } };
                _deleted++;
            }
        }

        private void Apply(int index, OldTaskShape old, OldTaskShape binSource)
        {
            SyncTaskItem item = _tasks[index];
            TaskItem task = item.Task;
            TaskFieldStamps stamps = item.Stamps;
            OldTaskShape projected = OldTaskShape.Projected(task, old.List, _book.Labels);
            int applied = 0;

            bool Newer(TaskField field)
            {
                return old.EditedAt > (stamps.Of(field)?.At ?? task.CreatedAt);
            }

            void Take(TaskField field, TaskItem changed, IReadOnlyList<string>? bases = null)
            {
                task = TaskFieldValues.With(task, changed, field);
                stamps = stamps.With(field, new TaskFieldStamp(old.EditedAt, _input.SenderId, bases));
                applied++;
            }

            if (!string.Equals(old.Title, projected.Title, StringComparison.Ordinal) && Newer(TaskField.Title))
            {
                Take(TaskField.Title, task with { Title = old.Title }, WithBase(stamps.Title?.Bases, TaskFieldValues.Hash(task.Title)));
            }

            if (old.HasDetails)
            {
                if (!string.Equals(old.Note, projected.Note, StringComparison.Ordinal) && Newer(TaskField.Note))
                {
                    Take(TaskField.Note, task with { Note = old.Note }, WithBase(stamps.Note?.Bases, TaskFieldValues.Hash(task.Note)));
                }

                if (!SameSubtasks(old.Subtasks, projected.Subtasks) && Newer(TaskField.Steps))
                {
                    Take(TaskField.Steps, task with { Steps = TaskSessionMapper.StepsFrom(old.Subtasks, task.Steps) });
                }

                if (old.EstimateMinutes != projected.EstimateMinutes && Newer(TaskField.Estimate))
                {
                    Take(TaskField.Estimate, task with { EstimateMinutes = old.EstimateMinutes });
                }

                if (old.PlannedFor?.TieText != projected.PlannedFor?.TieText && Newer(TaskField.Plan))
                {
                    Take(TaskField.Plan, task with { PlannedFor = old.PlannedFor });
                }
            }

            if (old.HasDueDate && old.DueOn != projected.DueOn && Newer(TaskField.Due))
            {
                Take(TaskField.Due, task with { DueOn = old.DueOn, DueAt = old.DueOn is null ? null : task.DueAt });
            }

            if (!SameCategory(old.Category, projected.Category) && Newer(TaskField.Label))
            {
                Take(TaskField.Label, task with { LabelId = _book.IdForName(old.Category, old.CategoryColor, old.EditedAt) });
            }

            IReadOnlyList<string>? oldTags = TaskLabels.Clean(old.Labels);
            if (TaskLabels.TieText(oldTags) != TaskLabels.TieText(projected.Labels) && Newer(TaskField.Tags))
            {
                Take(TaskField.Tags, task with { Tags = oldTags });
            }

            if (old.LocalTo != projected.LocalTo && Newer(TaskField.Devices))
            {
                Take(TaskField.Devices, task with { Devices = old.LocalTo is Guid keeper ? [keeper] : null });
            }

            if (binSource.EditedAt > (stamps.Bin?.At ?? task.CreatedAt) && BinChange(task, binSource) is TaskItem moved)
            {
                bool changedBin = moved.Bin != task.Bin;
                task = TaskFieldValues.With(task, moved, TaskField.Bin);
                stamps = stamps.With(TaskField.Bin, new TaskFieldStamp(binSource.EditedAt, _input.SenderId));
                applied++;
                if (changedBin)
                {
                    _moved++;
                }
            }

            if (applied == 0)
            {
                return;
            }

            DateTimeOffset newest = old.EditedAt > binSource.EditedAt ? old.EditedAt : binSource.EditedAt;
            task = task with
            {
                EditedAt = newest > task.EditedAt ? newest : task.EditedAt,
                TouchedAt = newest > task.TouchedAt ? newest : task.TouchedAt,
            };
            _tasks[index] = new SyncTaskItem(task, stamps);
            _fieldsApplied += applied;
        }

        // The bin part an old item asks for, or null when it asks for nothing new.
        private TaskItem? BinChange(TaskItem task, OldTaskShape old)
        {
            OldTaskList currentList = OldTaskFieldsWriter.ListOf(task.Bin);
            if (old.DoneAt is DateTimeOffset doneAt && task.DoneAt is null)
            {
                TaskBin before = currentList == old.List ? task.Bin : DefaultBin(old.List, old.BinMarker, countsInSession: false);
                return task with { Bin = TaskBin.Done, PrevBin = before, DoneAt = doneAt, Position = EndOf(TaskBin.Done) };
            }

            if (old.DoneAt is null && task.Bin == TaskBin.Done)
            {
                // Unticked: back where it was (Finished today travels in nextTasks whatever its bin was), This session only with room.
                TaskBin back = task.PrevBin ?? DefaultBin(old.List, old.BinMarker, countsInSession: true);
                if (back == TaskBin.Session && SessionActionsUsed() + task.ActionCount > _input.SessionActions)
                {
                    back = TaskBin.Queued;
                }

                return task with { Bin = back, PrevBin = null, DoneAt = null, Position = EndOf(back) };
            }

            if (currentList != old.List && task.Bin != TaskBin.Done)
            {
                TaskBin target = DefaultBin(old.List, old.BinMarker, countsInSession: true);
                return old.DoneAt is DateTimeOffset alsoDone
                    ? task with { Bin = TaskBin.Done, PrevBin = target, DoneAt = alsoDone, Position = EndOf(TaskBin.Done) }
                    : task with { Bin = target, PrevBin = null, Position = EndOf(target) };
            }

            if (currentList == old.List && (old.DoneAt is null) == (task.DoneAt is null))
            {
                double position = OldTaskFieldsWriter.NewPosition(task.Bin, old.Position);
                return position != task.Position ? task with { Position = position } : null;
            }

            return null;
        }

        private SyncTaskItem FromOld(OldTaskShape old, TaskItem? kept)
        {
            DateTimeOffset at = old.EditedAt;
            TaskItem task = kept ?? new TaskItem(old.Id, string.Empty, TaskBin.Sort, 0, old.CreatedAt ?? at, at, at);
            TaskBin open = kept?.PrevBin is TaskBin previous && OldTaskFieldsWriter.ListOf(previous) == old.List
                ? previous
                : DefaultBin(old.List, old.BinMarker, countsInSession: true);
            TaskBin bin = old.DoneAt is null ? open : TaskBin.Done;

            task = task with
            {
                Title = old.Title,
                Bin = bin,
                PrevBin = old.DoneAt is null ? null : open,
                DoneAt = old.DoneAt,
                Position = EndOf(bin),
                LabelId = _book.IdForName(old.Category, old.CategoryColor, at),
                Tags = TaskLabels.Clean(old.Labels),
                Devices = old.LocalTo is Guid keeper ? [keeper] : kept?.Devices,
                Origin = kept?.Origin ?? OriginOf(old),
                EditedAt = at,
                TouchedAt = at,
                DeletedAt = null,
                Conflicts = null,
            };

            if (old.HasDetails)
            {
                task = task with
                {
                    Steps = TaskSessionMapper.StepsFrom(old.Subtasks, kept?.Steps),
                    EstimateMinutes = old.EstimateMinutes,
                    Note = old.Note,
                    PlannedFor = old.PlannedFor,
                };
            }

            if (old.HasDueDate)
            {
                task = task with { DueOn = old.DueOn, DueAt = old.DueOn is null ? null : kept?.DueAt };
            }

            return new SyncTaskItem(task, TaskFieldStamps.AllAt(new TaskFieldStamp(at, _input.SenderId)));
        }

        private string? OriginOf(OldTaskShape old)
        {
            if (old.List == OldTaskList.Parked && old.CreatedAt is DateTimeOffset createdAt)
            {
                return TaskOrigins.Parked(createdAt, _input.Zone);
            }

            return string.Equals(_input.SenderKind, "phone", StringComparison.OrdinalIgnoreCase) ? TaskOrigins.FromPhone : null;
        }

        private TaskBin DefaultBin(OldTaskList list, string? marker, bool countsInSession)
        {
            return list switch
            {
                OldTaskList.NextTasks => countsInSession && SessionActionsUsed() + 1 <= _input.SessionActions ? TaskBin.Session : TaskBin.Queued,
                OldTaskList.Later => marker == SyncLaterItem.NextBin ? TaskBin.Next : TaskBin.Later,
                _ => TaskBin.Sort,
            };
        }

        private int SessionActionsUsed()
        {
            return _tasks.Where(item => item.Task.IsLive && item.Task.Bin == TaskBin.Session).Sum(item => item.Task.ActionCount);
        }

        private double EndOf(TaskBin bin)
        {
            List<double> positions = _tasks.Where(item => item.Task.IsLive && item.Task.Bin == bin).Select(item => item.Task.Position).ToList();
            return positions.Count == 0 ? 1 : positions.Max() + 1;
        }

        private void CloseHistory(int historyIndex, DateTimeOffset at)
        {
            if (historyIndex >= 0)
            {
                _history[historyIndex] = new SyncTaskLogItem(_history[historyIndex].Entry.Deleted(at));
            }
        }

        private OldTaskShape Clamped(OldTaskShape shape)
        {
            return shape with
            {
                EditedAt = shape.EditedAt > _limit ? _limit : shape.EditedAt,
                DeletedAt = shape.DeletedAt is DateTimeOffset deletedAt && deletedAt > _limit ? _limit : shape.DeletedAt,
            };
        }

        private static bool Differs(OldTaskShape old, OldTaskShape projected)
        {
            return !string.Equals(old.Title, projected.Title, StringComparison.Ordinal)
                || (old.DoneAt is null) != (projected.DoneAt is null)
                || !SameCategory(old.Category, projected.Category)
                || TaskLabels.TieText(TaskLabels.Clean(old.Labels)) != TaskLabels.TieText(projected.Labels)
                || (old.HasDetails && (!string.Equals(old.Note, projected.Note, StringComparison.Ordinal)
                    || !SameSubtasks(old.Subtasks, projected.Subtasks)
                    || old.EstimateMinutes != projected.EstimateMinutes))
                || (old.HasDueDate && old.DueOn != projected.DueOn);
        }

        private static bool SameCategory(string? first, string? second)
        {
            bool firstNone = string.IsNullOrWhiteSpace(first);
            bool secondNone = string.IsNullOrWhiteSpace(second);
            return firstNone || secondNone ? firstNone == secondNone : TaskLabel.SameName(first!, second!);
        }

        private static bool SameSubtasks(IReadOnlyList<SessionSubtask>? first, IReadOnlyList<SessionSubtask>? second)
        {
            IReadOnlyList<SessionSubtask> one = first ?? [];
            IReadOnlyList<SessionSubtask> other = second ?? [];
            return one.Count == other.Count
                && one.Zip(other).All(pair => pair.First.Id == pair.Second.Id
                    && string.Equals(pair.First.Title, pair.Second.Title, StringComparison.Ordinal)
                    && pair.First.DoneAt == pair.Second.DoneAt);
        }

        private static IReadOnlyList<string> WithBase(IReadOnlyList<string>? bases, string hash)
        {
            List<string> all = (bases ?? []).Where(other => other != hash).Append(hash).ToList();
            return all.Count > TaskFieldMerge.MaxBases ? all[^TaskFieldMerge.MaxBases..] : all;
        }
    }
}
