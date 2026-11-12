using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// What the user does to tasks in the one shared task list (ADR 76), pure: each action takes the lists and returns the new lists, the
/// toast text and the ids it touched (<see cref="TaskActionResult"/>), which is what <see cref="TaskUndoRules"/> keeps for Ctrl+Z.
/// Every change goes through <see cref="TaskEdits"/> as an edit by this device, so field-level merging and "untouched" (any edit,
/// move or step tick) work. The same actions serve one task and a selection (bulk). Tasks that are not live are skipped.
/// Moving into This session is not here: it goes through the session's cap rules (<c>SessionTasksService</c>), never around them.
/// </summary>
public static class TaskActions
{
    /// <summary>
    /// Moves tasks to <paramref name="bin"/>, each to the end of it in the order given, keeping id, number, steps and everything else.
    /// A step leaves focus outside This session. Moving to <see cref="TaskBin.Done"/> is <see cref="Finish"/>; This session is refused
    /// (nothing happens).
    /// </summary>
    public static TaskActionResult Move(TaskLists lists, IReadOnlyList<Guid> ids, TaskBin bin, DateTimeOffset now, Guid self)
    {
        if (bin == TaskBin.Session)
        {
            return TaskActionResult.None(lists);
        }

        if (bin == TaskBin.Done)
        {
            return Finish(lists, ids, now, self);
        }

        double end = TaskListRules.EndOf(lists, bin);
        List<Guid> movable = ids.Where(id => LiveOf(lists, id) is { } task && task.Bin != bin).ToList();
        (TaskLists after, List<TaskItem> changed) = Edit(
            lists, movable,
            (task, index) => task with
            {
                Bin = bin,
                PrevBin = task.Bin == TaskBin.Done ? task.PrevBin : task.Bin,
                Position = end + index,
                DoneAt = null,
                Steps = Unfocused(task.Steps),
            },
            [TaskField.Bin, TaskField.Steps], now, self);
        return Result(lists, after, changed, TaskToasts.Moved(LiveTasks(lists, movable), bin));
    }

    /// <summary>
    /// A new task typed in the add box or jotted down, at the end of <paramref name="bin"/> (To sort, Next, Later or Queued), with its
    /// label (one that isn't live is left off), tags, note and origin; its number comes when the lists are normalized. This session is
    /// refused, as in <see cref="Move"/>: the add box adds such a task to Queued and moves it through the session's cap. Finished today,
    /// a blank title or an id already taken do nothing. <paramref name="estimateMinutes"/> is its time limit (Settings › Tasks › Default
    /// time limit; null or 0 for none, held to <see cref="SessionChecklist.MaxEstimateMinutes"/>).
    /// </summary>
    public static TaskActionResult Add(
        TaskLists lists, Guid id, string title, TaskBin bin, Guid? labelId, IReadOnlyList<string>? tags, string? note, string? origin,
        DateTimeOffset now, Guid self, int? estimateMinutes = null)
    {
        string trimmed = title.Trim();
        if (trimmed.Length == 0 || bin is TaskBin.Session or TaskBin.Done || id == Guid.Empty || lists.Tasks.Any(item => item.Id == id))
        {
            return TaskActionResult.None(lists);
        }

        bool labelIsLive = labelId is Guid wanted && lists.Labels.Any(item => item.Label.IsLive && item.Label.Id == wanted);
        var task = new TaskItem(
            id, trimmed, bin, TaskListRules.EndOf(lists, bin), now, now, now,
            LabelId: labelIsLive ? labelId : null,
            Tags: TaskLabels.Clean(tags),
            EstimateMinutes: SessionChecklist.CleanEstimate(estimateMinutes),
            Note: SessionChecklist.CleanNote(note),
            Origin: string.IsNullOrWhiteSpace(origin) ? null : origin.Trim());
        List<SyncTaskItem> tasks = [.. lists.Tasks, new SyncTaskItem(task, TaskFieldStamps.AllAt(new TaskFieldStamp(now, self)))];
        return new TaskActionResult(lists with { Tasks = tasks }, TaskToasts.Added(task), [id]);
    }

    /// <summary>
    /// Puts one task before another in its bin (or at the end when <paramref name="beforeId"/> is null): a place between the two
    /// neighbours, nothing else changes.
    /// </summary>
    public static TaskActionResult Reorder(TaskLists lists, Guid id, Guid? beforeId, DateTimeOffset now, Guid self)
    {
        if (LiveOf(lists, id) is not TaskItem moving)
        {
            return TaskActionResult.None(lists);
        }

        List<TaskItem> others = lists.Tasks.Select(item => item.Task)
            .Where(task => task.IsLive && task.Bin == moving.Bin && task.Id != id)
            .OrderBy(task => task.Position).ToList();
        int at = beforeId is Guid before ? others.FindIndex(task => task.Id == before) : others.Count;
        if (at < 0)
        {
            return TaskActionResult.None(lists);
        }

        double position = SharedListMerge.PositionBetween(at > 0 ? others[at - 1].Position : null, at < others.Count ? others[at].Position : null);
        (TaskLists after, List<TaskItem> changed) = Edit(lists, [id], (task, _) => task with { Position = position }, [TaskField.Bin], now, self);
        return Result(lists, after, changed, "Moved");
    }

    /// <summary>Ticks tasks: each goes to Finished today (<see cref="TaskBin.Done"/>) remembering the bin it left; a finished Shelf task settles into History on normalizing.</summary>
    public static TaskActionResult Finish(TaskLists lists, IReadOnlyList<Guid> ids, DateTimeOffset now, Guid self)
    {
        double end = TaskListRules.EndOf(lists, TaskBin.Done);
        List<Guid> open = ids.Where(id => LiveOf(lists, id) is { Bin: not TaskBin.Done }).ToList();
        (TaskLists after, List<TaskItem> changed) = Edit(
            lists, open,
            (task, index) => task with { Bin = TaskBin.Done, PrevBin = task.Bin, Position = end + index, DoneAt = now },
            [TaskField.Bin], now, self);
        return Result(lists, after, changed, TaskToasts.Finished(LiveTasks(lists, open)));
    }

    /// <summary>Unticks finished tasks: each goes back to the bin it left (Queued when that was This session or is unknown), at the end of it.</summary>
    public static TaskActionResult Reopen(TaskLists lists, IReadOnlyList<Guid> ids, DateTimeOffset now, Guid self)
    {
        List<Guid> done = ids.Where(id => LiveOf(lists, id) is { Bin: TaskBin.Done }).ToList();
        (TaskLists after, List<TaskItem> changed) = Edit(lists, done, (task, index) => Reopened(lists, task, index), [TaskField.Bin], now, self);
        string toast = changed.Count == 1 ? TaskToasts.BackIn(changed[0].Bin) : changed.Count + " tasks reopened";
        return Result(lists, after, changed, toast);
    }

    /// <summary>
    /// Ticks or unticks a step. Ticking the last open step finishes the task ("All steps done"); unticking a step of a finished task
    /// puts the task back where it was before it finished (spec 05, "Steps finish the task").
    /// </summary>
    public static TaskActionResult TickStep(TaskLists lists, Guid taskId, Guid stepId, DateTimeOffset now, Guid self)
    {
        if (LiveOf(lists, taskId) is not TaskItem task)
        {
            return TaskActionResult.None(lists);
        }

        List<TaskStep> steps = (task.Steps ?? []).ToList();
        int index = steps.FindIndex(step => step.Id == stepId);
        if (index < 0)
        {
            return TaskActionResult.None(lists);
        }

        bool ticking = !steps[index].IsDone;
        List<TaskStep> ticked = steps.Select(step => step.Id == stepId ? step with { DoneAt = ticking ? now : null } : step).ToList();
        bool allDone = ticking && ticked.All(step => step.IsDone);
        string reference = TaskToasts.StepRef(task, index);
        string toast = allDone && task.Bin != TaskBin.Done ? TaskToasts.AllStepsDone(task) : "Step " + reference + (ticking ? " done" : " reopened");

        (TaskLists after, List<TaskItem> changed) = Edit(
            lists, [taskId],
            (current, _) =>
            {
                TaskItem target = current with { Steps = ticked };
                if (allDone && current.Bin != TaskBin.Done)
                {
                    return target with { Bin = TaskBin.Done, PrevBin = current.Bin, Position = TaskListRules.EndOf(lists, TaskBin.Done), DoneAt = now };
                }

                return !ticking && current.Bin == TaskBin.Done ? Reopened(lists, target, 0) : target;
            },
            [TaskField.Steps, TaskField.Bin], now, self);
        return Result(lists, after, changed, toast);
    }

    /// <summary>Adds a step (after <paramref name="afterStepId"/>, else at the end); a finished task is unticked and goes back where it was.</summary>
    public static TaskActionResult AddStep(TaskLists lists, Guid taskId, string title, Guid? afterStepId, DateTimeOffset now, Guid self)
    {
        string text = title.Trim();
        if (text.Length == 0 || LiveOf(lists, taskId) is null)
        {
            return TaskActionResult.None(lists);
        }

        (TaskLists after, List<TaskItem> changed) = Edit(
            lists, [taskId],
            (task, _) =>
            {
                List<TaskStep> steps = (task.Steps ?? []).ToList();
                int at = afterStepId is Guid previous ? steps.FindIndex(step => step.Id == previous) + 1 : steps.Count;
                steps.Insert(at < 0 ? steps.Count : at, new TaskStep(Guid.NewGuid(), text));
                TaskItem target = task with { Steps = steps };
                return task.Bin == TaskBin.Done ? Reopened(lists, target, 0) : target;
            },
            [TaskField.Steps, TaskField.Bin], now, self);
        return Result(lists, after, changed, "Step added");
    }

    /// <summary>Renames a step (a blank or unchanged name does nothing).</summary>
    public static TaskActionResult RenameStep(TaskLists lists, Guid taskId, Guid stepId, string title, DateTimeOffset now, Guid self)
    {
        string text = title.Trim();
        if (text.Length == 0 || LiveOf(lists, taskId)?.Steps?.FirstOrDefault(step => step.Id == stepId) is not { } step || step.Title == text)
        {
            return TaskActionResult.None(lists);
        }

        (TaskLists after, List<TaskItem> changed) = Edit(
            lists, [taskId], (task, _) => task with { Steps = (task.Steps ?? []).Select(item => item.Id == stepId ? item with { Title = text } : item).ToList() },
            [TaskField.Steps], now, self);
        return Result(lists, after, changed, "Step renamed");
    }

    /// <summary>Takes a step off its task (the last one leaves the task without steps).</summary>
    public static TaskActionResult RemoveStep(TaskLists lists, Guid taskId, Guid stepId, DateTimeOffset now, Guid self)
    {
        if (LiveOf(lists, taskId)?.Steps?.Any(step => step.Id == stepId) != true)
        {
            return TaskActionResult.None(lists);
        }

        (TaskLists after, List<TaskItem> changed) = Edit(
            lists, [taskId],
            (task, _) =>
            {
                List<TaskStep> left = (task.Steps ?? []).Where(step => step.Id != stepId).ToList();
                return task with { Steps = left.Count == 0 ? null : left };
            },
            [TaskField.Steps], now, self);
        return Result(lists, after, changed, "Step removed");
    }

    /// <summary>
    /// Puts a step before or after another step of the same task (drag, Alt+↑/↓). Its letter follows its new place; nothing else
    /// changes.
    /// </summary>
    public static TaskActionResult MoveStep(TaskLists lists, Guid taskId, Guid stepId, Guid targetStepId, bool after, DateTimeOffset now, Guid self)
    {
        List<TaskStep> steps = (LiveOf(lists, taskId)?.Steps ?? []).ToList();
        TaskStep? moving = steps.FirstOrDefault(step => step.Id == stepId);
        if (moving is null || stepId == targetStepId || steps.All(step => step.Id != targetStepId))
        {
            return TaskActionResult.None(lists);
        }

        steps.Remove(moving);
        int at = steps.FindIndex(step => step.Id == targetStepId) + (after ? 1 : 0);
        steps.Insert(at, moving);
        (TaskLists result, List<TaskItem> changed) = Edit(lists, [taskId], (task, _) => task with { Steps = steps }, [TaskField.Steps], now, self);
        return Result(lists, result, changed, "Step moved");
    }

    /// <summary>Sets the private note (blank clears it). Cleaned as the checklist does.</summary>
    public static TaskActionResult SetNote(TaskLists lists, Guid id, string? note, DateTimeOffset now, Guid self)
    {
        string? clean = SessionChecklist.CleanNote(note);
        (TaskLists after, List<TaskItem> changed) = Edit(lists, [id], (task, _) => task with { Note = clean }, [TaskField.Note], now, self);
        return Result(lists, after, changed, "Note saved");
    }

    /// <summary>
    /// The task list's half of moving a Queued task of the next session's list into This session, while no session runs: its bin
    /// becomes This session (at the end). Only <c>TaskActionsService.MoveToSessionAsync</c> calls this, after the session's cap said
    /// there is room; it is never a way around the cap.
    /// </summary>
    public static TaskActionResult PlaceInSession(TaskLists lists, Guid id, DateTimeOffset now, Guid self)
    {
        if (LiveOf(lists, id) is not { Bin: TaskBin.Queued })
        {
            return TaskActionResult.None(lists);
        }

        double end = TaskListRules.EndOf(lists, TaskBin.Session);
        (TaskLists after, List<TaskItem> changed) = Edit(
            lists, [id], (task, _) => task with { Bin = TaskBin.Session, PrevBin = task.Bin, Position = end }, [TaskField.Bin], now, self);
        return Result(lists, after, changed, "1 task → This session");
    }

    /// <summary>Deletes tasks: each is kept in History as "deleted" with everything it had (so "Bring back" revives it) and leaves the list.</summary>
    public static TaskActionResult Delete(TaskLists lists, IReadOnlyList<Guid> ids, DateTimeOffset now, Guid self)
    {
        return TaskHistoryRules.Remove(lists, ids, TaskHistoryKind.Deleted, now, self);
    }

    /// <summary>Archives tasks: out of the way, kept in History as "archived".</summary>
    public static TaskActionResult Archive(TaskLists lists, IReadOnlyList<Guid> ids, DateTimeOffset now, Guid self)
    {
        return TaskHistoryRules.Remove(lists, ids, TaskHistoryKind.Archived, now, self);
    }

    /// <summary>Renames one task (a blank or unchanged title does nothing).</summary>
    public static TaskActionResult Rename(TaskLists lists, Guid id, string title, DateTimeOffset now, Guid self)
    {
        return RenameEach(lists, new Dictionary<Guid, string> { [id] = title }, now, self);
    }

    /// <summary>
    /// "Rename one by one" in one go: every task with a new title changes together, so the dialog saves and syncs nothing until its
    /// last name is confirmed. Blank or unchanged names are skipped.
    /// </summary>
    public static TaskActionResult RenameEach(TaskLists lists, IReadOnlyDictionary<Guid, string> titles, DateTimeOffset now, Guid self)
    {
        Dictionary<Guid, string> wanted = titles
            .Where(pair => pair.Value.Trim().Length > 0 && LiveOf(lists, pair.Key) is { } task && task.Title != pair.Value.Trim())
            .ToDictionary(pair => pair.Key, pair => pair.Value.Trim());
        (TaskLists after, List<TaskItem> changed) = Edit(lists, wanted.Keys.ToList(), (task, _) => task with { Title = wanted[task.Id] }, [TaskField.Title], now, self);
        return Result(lists, after, changed, TaskToasts.Renamed(changed.Count));
    }

    /// <summary>Gives tasks one label, or none (<paramref name="labelId"/> null). An unknown label does nothing.</summary>
    public static TaskActionResult SetLabel(TaskLists lists, IReadOnlyList<Guid> ids, Guid? labelId, DateTimeOffset now, Guid self)
    {
        TaskLabel? label = labelId is Guid wanted ? lists.Labels.Select(item => item.Label).FirstOrDefault(item => item.IsLive && item.Id == wanted) : null;
        if (labelId is not null && label is null)
        {
            return TaskActionResult.None(lists);
        }

        (TaskLists after, List<TaskItem> changed) = Edit(lists, ids, (task, _) => task with { LabelId = labelId }, [TaskField.Label], now, self);
        return Result(lists, after, changed, TaskToasts.Labelled(label?.Name));
    }

    /// <summary>Adds tags to tasks (cleaned, no duplicates).</summary>
    public static TaskActionResult AddTags(TaskLists lists, IReadOnlyList<Guid> ids, IReadOnlyList<string> tags, DateTimeOffset now, Guid self)
    {
        IReadOnlyList<string> clean = TaskLabels.Clean(tags) ?? [];
        (TaskLists after, List<TaskItem> changed) = Edit(lists, ids, (task, _) => task with { Tags = TaskLabels.With(task.Tags, clean) }, [TaskField.Tags], now, self);
        return Result(lists, after, changed, TaskToasts.Tagged(clean, added: true));
    }

    /// <summary>Takes tags off tasks.</summary>
    public static TaskActionResult RemoveTags(TaskLists lists, IReadOnlyList<Guid> ids, IReadOnlyList<string> tags, DateTimeOffset now, Guid self)
    {
        IReadOnlyList<string> clean = TaskLabels.Clean(tags) ?? [];
        (TaskLists after, List<TaskItem> changed) = Edit(
            lists, ids, (task, _) => task with { Tags = clean.Aggregate(task.Tags, (current, tag) => TaskLabels.Without(current, tag)) }, [TaskField.Tags], now, self);
        return Result(lists, after, changed, TaskToasts.Tagged(clean, added: false));
    }

    /// <summary>The bulk Tags menu's tick: when every task has the tag it comes off all of them, otherwise it goes on all.</summary>
    public static TaskActionResult ToggleTag(TaskLists lists, IReadOnlyList<Guid> ids, string tag, DateTimeOffset now, Guid self)
    {
        List<TaskItem> tasks = LiveTasks(lists, ids);
        bool all = tasks.Count > 0 && tasks.All(task => TaskLabels.Has(task.Tags, tag));
        return all ? RemoveTags(lists, ids, [tag], now, self) : AddTags(lists, ids, [tag], now, self);
    }

    /// <summary>Sets the time limit in minutes (null: none). Out of range values are cleaned as the checklist does.</summary>
    public static TaskActionResult SetTimeLimit(TaskLists lists, IReadOnlyList<Guid> ids, int? minutes, DateTimeOffset now, Guid self)
    {
        int? clean = SessionChecklist.CleanEstimate(minutes);
        (TaskLists after, List<TaskItem> changed) = Edit(lists, ids, (task, _) => task with { EstimateMinutes = clean }, [TaskField.Estimate], now, self);
        return Result(lists, after, changed, TaskToasts.TimeLimit(clean));
    }

    public static TaskActionResult SetHighlight(TaskLists lists, IReadOnlyList<Guid> ids, bool on, DateTimeOffset now, Guid self)
    {
        (TaskLists after, List<TaskItem> changed) = Edit(lists, ids, (task, _) => task with { Highlight = on }, [TaskField.Highlight], now, self);
        return Result(lists, after, changed, TaskToasts.Highlighted(on));
    }

    /// <summary>The H key: when every task is highlighted the highlight comes off, otherwise all get it.</summary>
    public static TaskActionResult ToggleHighlight(TaskLists lists, IReadOnlyList<Guid> ids, DateTimeOffset now, Guid self)
    {
        List<TaskItem> tasks = LiveTasks(lists, ids);
        return SetHighlight(lists, ids, !(tasks.Count > 0 && tasks.All(task => task.Highlight)), now, self);
    }

    // ---- Reminders & due date (spec 05 §7): each edit is stamped; the first reminder or due date takes the calendar default ----

    /// <summary>
    /// Adds a reminder: one of the relative rules (<see cref="TaskReminder.AtStart"/>, <see cref="TaskReminder.TenMinutesBefore"/>,
    /// <see cref="TaskReminder.OneHourBefore"/>, <see cref="TaskReminder.DayBeforeAtNine"/>) or <see cref="TaskReminder.Absolute"/> at
    /// <paramref name="at"/>. Refused at <see cref="TaskReminder.MaxPerTask"/>, for a relative rule the task already has (or the same
    /// moment twice), for an unknown rule and for an absolute one without a moment. A task's first reminder or due date takes
    /// "Add new reminders to" (<see cref="TaskCalendarOptIn.WithDefault"/>).
    /// </summary>
    public static TaskActionResult AddReminder(
        TaskLists lists, Guid id, Guid reminderId, string rule, DateTimeOffset? at, TaskCalendarDestination destination, DateTimeOffset now, Guid self)
    {
        bool known = rule is TaskReminder.AtStart or TaskReminder.TenMinutesBefore or TaskReminder.OneHourBefore or TaskReminder.DayBeforeAtNine
            || rule == TaskReminder.Absolute && at is not null;
        if (!known || LiveOf(lists, id) is not TaskItem task)
        {
            return TaskActionResult.None(lists);
        }

        IReadOnlyList<TaskReminder> reminders = task.Reminders ?? [];
        bool repeated = reminders.Any(reminder => reminder.Rule == rule && (rule != TaskReminder.Absolute || reminder.At == at));
        if (reminders.Count >= TaskReminder.MaxPerTask || repeated || reminders.Any(reminder => reminder.Id == reminderId))
        {
            return TaskActionResult.None(lists);
        }

        var added = new TaskReminder(reminderId, rule, rule == TaskReminder.Absolute ? at : null);
        (TaskLists after, List<TaskItem> changed) = Edit(
            lists, [id], (current, _) => TaskCalendarOptIn.WithDefault(current, current with { Reminders = [.. current.Reminders ?? [], added] }, destination),
            [TaskField.Reminders, TaskField.Calendar], now, self);
        return Result(lists, after, changed, changed.Count == 1 ? TaskToasts.ReminderAdded(changed[0]) : string.Empty);
    }

    /// <summary>Takes one reminder off a task (the last one leaves it without reminders).</summary>
    public static TaskActionResult RemoveReminder(TaskLists lists, Guid id, Guid reminderId, DateTimeOffset now, Guid self)
    {
        if (LiveOf(lists, id)?.Reminders?.Any(reminder => reminder.Id == reminderId) != true)
        {
            return TaskActionResult.None(lists);
        }

        (TaskLists after, List<TaskItem> changed) = Edit(
            lists, [id],
            (task, _) =>
            {
                List<TaskReminder> left = (task.Reminders ?? []).Where(reminder => reminder.Id != reminderId).ToList();
                return task with { Reminders = left.Count == 0 ? null : left };
            },
            [TaskField.Reminders], now, self);
        return Result(lists, after, changed, TaskToasts.ReminderRemoved);
    }

    /// <summary>
    /// Sets "Done by": a date, with a time or for the whole day (<paramref name="at"/> null); <paramref name="on"/> null removes it (and
    /// its time). A task's first due date or reminder takes "Add new reminders to" (<see cref="TaskCalendarOptIn.WithDefault"/>).
    /// </summary>
    public static TaskActionResult SetDue(
        TaskLists lists, IReadOnlyList<Guid> ids, DateOnly? on, TimeOnly? at, TaskCalendarDestination destination, DateTimeOffset now, Guid self)
    {
        TimeOnly? time = on is null ? null : at;
        (TaskLists after, List<TaskItem> changed) = Edit(
            lists, ids, (task, _) => TaskCalendarOptIn.WithDefault(task, task with { DueOn = on, DueAt = time }, destination),
            [TaskField.Due, TaskField.Calendar], now, self);
        return Result(lists, after, changed, TaskToasts.Due(on, time));
    }

    /// <summary>"Can't miss": the task's reminders ring full screen and must be held to dismiss.</summary>
    public static TaskActionResult SetCantMiss(TaskLists lists, IReadOnlyList<Guid> ids, bool on, DateTimeOffset now, Guid self)
    {
        (TaskLists after, List<TaskItem> changed) = Edit(lists, ids, (task, _) => task with { CantMiss = on }, [TaskField.CantMiss], now, self);
        return Result(lists, after, changed, TaskToasts.CantMiss(on));
    }

    /// <summary>The task's own "Add to Google Calendar" switch (ADR 78; <see cref="TaskItem.Calendar"/>).</summary>
    public static TaskActionResult SetCalendar(TaskLists lists, IReadOnlyList<Guid> ids, bool on, DateTimeOffset now, Guid self)
    {
        (TaskLists after, List<TaskItem> changed) = Edit(lists, ids, (task, _) => task with { Calendar = on }, [TaskField.Calendar], now, self);
        return Result(lists, after, changed, TaskToasts.Calendar(on));
    }

    // ---- "Changed on both devices" (spec 05 §11): the user keeps one version, or both as two tasks ----

    /// <summary>
    /// Keeps one version of a title or note that two devices changed (<paramref name="conflict"/> on the task): the shown one, or the
    /// other one (<paramref name="keepOther"/>). Stamped as made from both versions (<see cref="TaskEdits.Resolve"/>), so the conflict
    /// closes on every device once synced. A blank other title keeps the shown one. Nothing happens when the task or the conflict is gone.
    /// </summary>
    public static TaskActionResult ResolveConflict(TaskLists lists, Guid id, TaskConflict conflict, bool keepOther, DateTimeOffset now, Guid self)
    {
        List<SyncTaskItem> tasks = lists.Tasks.ToList();
        int index = tasks.FindIndex(item => item.Id == id && item.Task.IsLive);
        if (index < 0 || tasks[index].Task.Conflicts?.Contains(conflict) != true)
        {
            return TaskActionResult.None(lists);
        }

        SyncTaskItem item = tasks[index];
        bool isTitle = conflict.Field == TaskConflict.TitleField;
        string? shown = isTitle ? item.Task.Title : item.Task.Note;
        bool keptOther = keepOther;
        if (keptOther && isTitle && string.IsNullOrWhiteSpace(conflict.OtherValue))
        {
            keptOther = false;
        }

        string? kept = keptOther ? conflict.OtherValue : shown;
        tasks[index] = TaskEdits.Resolve(item, conflict, isTitle ? kept?.Trim() : SessionChecklist.CleanNote(kept), now, self);
        return new TaskActionResult(lists with { Tasks = tasks }, keptOther ? "Kept the other version" : "Kept this version", [id]);
    }

    /// <summary>
    /// "Keep both as separate tasks": the shown version stays on the task (the conflict closes as in <see cref="ResolveConflict"/>) and
    /// the other version becomes a new task <paramref name="newId"/> at the end of Queued, with the task's label, tags, time limit and
    /// steps (new step ids, none in focus), and on the same devices (a local or limited task's copy stays where the task is, ADR 72).
    /// Reminders, due date and highlight stay with the original only, so nothing rings twice.
    /// </summary>
    public static TaskActionResult KeepBothVersions(TaskLists lists, Guid id, TaskConflict conflict, Guid newId, DateTimeOffset now, Guid self)
    {
        if (newId == Guid.Empty || lists.Tasks.Any(item => item.Id == newId))
        {
            return TaskActionResult.None(lists);
        }

        TaskActionResult resolved = ResolveConflict(lists, id, conflict, keepOther: false, now, self);
        if (!resolved.Changed || LiveOf(resolved.Lists, id) is not TaskItem source)
        {
            return TaskActionResult.None(lists);
        }

        bool isTitle = conflict.Field == TaskConflict.TitleField;
        string title = isTitle && conflict.OtherValue?.Trim() is { Length: > 0 } other ? other : source.Title;
        string? note = isTitle ? source.Note : SessionChecklist.CleanNote(conflict.OtherValue);
        List<TaskStep>? steps = source.Steps?.Select(step => step with { Id = Guid.NewGuid(), Focus = false }).ToList();
        var copy = new TaskItem(
            newId, title, TaskBin.Queued, TaskListRules.EndOf(resolved.Lists, TaskBin.Queued), now, now, now,
            LabelId: source.LabelId,
            Tags: source.Tags,
            Steps: steps is { Count: > 0 } ? steps : null,
            EstimateMinutes: source.EstimateMinutes,
            Note: note,
            Origin: source.Number is TaskNumber number ? "Other version of " + number.Value : "Other version",
            Devices: source.Devices);
        List<SyncTaskItem> tasks = [.. resolved.Lists.Tasks, new SyncTaskItem(copy, TaskFieldStamps.AllAt(new TaskFieldStamp(now, self)))];
        return new TaskActionResult(resolved.Lists with { Tasks = tasks }, "Kept both · the other copy is in Queued", [id, newId]);
    }

    /// <summary>
    /// A task was opened (its details shown) or kept ("Keep" in the tidy-up): it counts as touched, which resets its age. Only the
    /// touch time moves (never undone, never a toast). Nothing happens when it was already touched at or after <paramref name="now"/>.
    /// </summary>
    public static TaskActionResult Touch(TaskLists lists, Guid id, DateTimeOffset now)
    {
        List<SyncTaskItem> tasks = lists.Tasks.ToList();
        int index = tasks.FindIndex(item => item.Id == id && item.Task.IsLive);
        if (index < 0 || tasks[index].Task.TouchedAt >= now)
        {
            return TaskActionResult.None(lists);
        }

        SyncTaskItem item = tasks[index];
        tasks[index] = item with { Task = item.Task with { TouchedAt = now, EditedAt = SharedListMerge.NextStamp(now, item) } };
        return new TaskActionResult(lists with { Tasks = tasks }, string.Empty, [id]);
    }

    // The bin a finished task goes back to, at the end of it.
    private static TaskItem Reopened(TaskLists lists, TaskItem task, int index)
    {
        TaskBin bin = task.PrevBin is { } previous && previous is not (TaskBin.Done or TaskBin.Session) ? previous : TaskBin.Queued;
        return task with { Bin = bin, PrevBin = TaskBin.Done, Position = TaskListRules.EndOf(lists, bin) + index, DoneAt = null };
    }

    private static TaskItem? LiveOf(TaskLists lists, Guid id)
    {
        SyncTaskItem? item = lists.Tasks.FirstOrDefault(candidate => candidate.Id == id);
        return item is { Task.IsLive: true } ? item.Task : null;
    }

    private static List<TaskItem> LiveTasks(TaskLists lists, IEnumerable<Guid> ids)
    {
        return ids.Distinct().Select(id => LiveOf(lists, id)).OfType<TaskItem>().ToList();
    }

    private static IReadOnlyList<TaskStep>? Unfocused(IReadOnlyList<TaskStep>? steps)
    {
        return steps is null || steps.All(step => !step.Focus) ? steps : steps.Select(step => step with { Focus = false }).ToList();
    }

    // Changes each live task of <ids> (in order) with <change> and stamps the named fields as this device's edit; the tasks that changed come back as they are now.
    private static (TaskLists Lists, List<TaskItem> Changed) Edit(
        TaskLists lists, IEnumerable<Guid> ids, Func<TaskItem, int, TaskItem> change, TaskField[] fields, DateTimeOffset now, Guid self)
    {
        List<SyncTaskItem> tasks = lists.Tasks.ToList();
        var changed = new List<TaskItem>();
        int number = 0;
        foreach (Guid id in ids.Distinct())
        {
            int index = tasks.FindIndex(item => item.Id == id && item.Task.IsLive);
            if (index < 0)
            {
                continue;
            }

            SyncTaskItem item = tasks[index];
            TaskItem target = change(item.Task, number++);
            SyncTaskItem edited = item;
            foreach (TaskField field in fields)
            {
                edited = TaskEdits.Edit(edited, field, target, now, self);
            }

            if (!ReferenceEquals(edited, item))
            {
                tasks[index] = edited;
                changed.Add(edited.Task);
            }
        }

        return (lists with { Tasks = tasks }, changed);
    }

    private static TaskActionResult Result(TaskLists before, TaskLists after, List<TaskItem> changed, string toast)
    {
        return changed.Count == 0 ? TaskActionResult.None(before) : new TaskActionResult(after, toast, changed.Select(task => task.Id).ToList());
    }
}
