using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// The session cap counted in actions (ADR 76), on the running checklist (<see cref="SessionChecklist"/>), kept pure so it can be
/// checked without the app:
/// <list type="bullet">
/// <item>A session holds <see cref="DefaultCap"/> actions (the Tasks setting, <see cref="MinCap"/> to <see cref="MaxCap"/>), plus the
/// "Add anyway" adds approved for it (with their reason, as before).</item>
/// <item>A stepless task is one action. A task with steps is one action per open step in focus (<see cref="SessionChecklist.FocusedSteps"/>),
/// and one when none is in focus (its heading holds the place). A finished task still holds one place, as a finished task always
/// counted toward the three before: finishing never makes room for more.</item>
/// <item>A task with steps that joins gets its first open steps in focus, filling the free places (at least one).</item>
/// <item>When a focused step is finished, the next open step of the same task takes its place (the setting, on by default); a
/// finished step leaves focus either way.</item>
/// <item>"This session is full": swapping a focused step out takes it out of focus (the task stays while it has another in focus),
/// swapping a task out takes it off the checklist (counted as taken off open, ADR 25) so the caller can put it in Queued.</item>
/// </list>
/// None of this changes <see cref="SessionChecklist.AllDone"/>: "All tasks done" (ADR 25) still needs every task with every step ticked.
/// </summary>
public static class SessionActions
{
    public const int MinCap = 2;
    public const int MaxCap = 5;
    public const int DefaultCap = 3;

    /// <summary>A cap as it is kept: <see cref="MinCap"/> to <see cref="MaxCap"/>.</summary>
    public static int ClampCap(int cap)
    {
        return Math.Clamp(cap, MinCap, MaxCap);
    }

    /// <summary>The places <paramref name="task"/> holds (see the class summary).</summary>
    public static int ActionsOf(SessionTask task, IReadOnlyCollection<Guid>? focused)
    {
        if (task.IsDone || task.Subtasks.Count == 0)
        {
            return 1;
        }

        int inFocus = task.Subtasks.Count(step => !step.IsDone && focused is not null && focused.Contains(step.Id));
        return Math.Max(1, inFocus);
    }

    /// <summary>The places the checklist's tasks hold.</summary>
    public static int Used(SessionChecklist checklist)
    {
        IReadOnlyCollection<Guid> focused = FocusSet(checklist);
        return checklist.Tasks.Sum(task => ActionsOf(task, focused));
    }

    /// <summary>The places still free under <paramref name="allowance"/> (the cap plus approved "Add anyway" adds); never below 0.</summary>
    public static int Free(SessionChecklist checklist, int allowance)
    {
        return Math.Max(0, allowance - Used(checklist));
    }

    /// <summary>True when the step <paramref name="stepId"/> is an open step in focus.</summary>
    public static bool IsFocused(SessionChecklist checklist, Guid stepId)
    {
        return checklist.FocusedSteps?.Contains(stepId) == true && OpenStep(checklist, stepId) is not null;
    }

    /// <summary>
    /// The session's numbered places in the checklist's order (Quiet view: one row per place). Finished tasks hold a place but have
    /// no number, so they are not listed.
    /// </summary>
    public static IReadOnlyList<SessionSlot> Slots(SessionChecklist checklist)
    {
        IReadOnlyCollection<Guid> focused = FocusSet(checklist);
        var slots = new List<SessionSlot>();
        foreach (SessionTask task in checklist.Tasks.Where(task => !task.IsDone))
        {
            List<SessionSubtask> inFocus = task.Subtasks.Where(step => !step.IsDone && focused.Contains(step.Id)).ToList();
            if (inFocus.Count == 0)
            {
                slots.Add(new SessionSlot(slots.Count + 1, task.Id, null));
                continue;
            }

            foreach (SessionSubtask step in inFocus)
            {
                slots.Add(new SessionSlot(slots.Count + 1, task.Id, step.Id));
            }
        }

        return slots;
    }

    /// <summary>
    /// <paramref name="joining"/> (tasks of the checklist that just joined it) get their first open steps in focus, filling the free
    /// places in the checklist's order: at least one step each, more while places are free. A task that already has one in focus,
    /// a finished one and a stepless one are left as they are.
    /// </summary>
    public static SessionChecklist Joined(SessionChecklist checklist, IEnumerable<Guid> joining, int allowance)
    {
        var newcomers = joining.ToHashSet();
        SessionChecklist result = checklist;
        foreach (SessionTask task in checklist.Tasks.Where(task => newcomers.Contains(task.Id) && !task.IsDone && task.Subtasks.Count > 0))
        {
            IReadOnlyCollection<Guid> focused = FocusSet(result);
            if (task.Subtasks.Any(step => !step.IsDone && focused.Contains(step.Id)))
            {
                continue;
            }

            // The task holds one place already; the free places come on top of it.
            List<SessionSubtask> open = task.Subtasks.Where(step => !step.IsDone).ToList();
            int wanted = Math.Max(1, Math.Min(Free(result, allowance) + 1, open.Count));
            result = WithFocus(result, focused.Concat(open.Take(wanted).Select(step => step.Id)));
        }

        return result;
    }

    /// <summary>
    /// Ticks or unticks the task or step <paramref name="id"/> (<see cref="SessionChecklist.Toggle"/>). A focused step that is
    /// finished leaves focus, and with <paramref name="nextStepTakesSlot"/> the next open step of its task (after it, else the first)
    /// takes its place. An unticked step comes back out of focus (its task holds a place either way).
    /// </summary>
    public static SessionChecklist Ticked(SessionChecklist checklist, Guid id, DateTimeOffset now, bool nextStepTakesSlot)
    {
        bool wasFocusedOpen = IsFocused(checklist, id);
        SessionChecklist toggled = checklist.Toggle(id, now);
        if (!wasFocusedOpen || OpenStep(toggled, id) is not null)
        {
            return Cleaned(toggled);
        }

        List<Guid> focused = (toggled.FocusedSteps ?? []).Where(step => step != id).ToList();
        SessionTask? owner = toggled.Tasks.FirstOrDefault(task => !task.IsDone && task.Subtasks.Any(step => step.Id == id));
        if (nextStepTakesSlot && owner is not null)
        {
            int at = owner.Subtasks.ToList().FindIndex(step => step.Id == id);
            SessionSubtask? next = owner.Subtasks.Skip(at + 1).FirstOrDefault(step => !step.IsDone && !focused.Contains(step.Id))
                                   ?? owner.Subtasks.FirstOrDefault(step => !step.IsDone && !focused.Contains(step.Id));
            if (next is not null)
            {
                focused.Add(next.Id);
            }
        }

        return Cleaned(WithFocus(toggled, focused));
    }

    /// <summary>
    /// Brings the open step <paramref name="stepId"/> into focus. It takes a free place, except as its task's first focused step (the
    /// task held that place already). With no free place nothing changes and the answer is <see cref="SessionFocusResult.Full"/>.
    /// </summary>
    public static (SessionChecklist Checklist, SessionFocusResult Result) Focus(SessionChecklist checklist, Guid stepId, int allowance)
    {
        SessionTask? owner = OpenStep(checklist, stepId);
        if (owner is null)
        {
            return (checklist, SessionFocusResult.NotFound);
        }

        IReadOnlyCollection<Guid> focused = FocusSet(checklist);
        if (focused.Contains(stepId))
        {
            return (checklist, SessionFocusResult.Focused);
        }

        bool firstOfTask = !owner.Subtasks.Any(step => !step.IsDone && focused.Contains(step.Id));
        if (!firstOfTask && Free(checklist, allowance) == 0)
        {
            return (checklist, SessionFocusResult.Full);
        }

        return (WithFocus(checklist, focused.Append(stepId)), SessionFocusResult.Focused);
    }

    /// <summary>
    /// "This session is full", Swap: the place <paramref name="slot"/> is made free. A focused step leaves focus while its task keeps
    /// another in focus; otherwise the whole task leaves the checklist (<see cref="SessionChecklist.Remove"/>, so an open one counts as
    /// taken off open, ADR 25) and its id comes back for the caller to put in Queued.
    /// </summary>
    public static (SessionChecklist Checklist, Guid? Leaving) SwapOut(SessionChecklist checklist, SessionSlot slot)
    {
        SessionTask? task = checklist.Tasks.FirstOrDefault(candidate => candidate.Id == slot.TaskId);
        if (task is null || task.IsDone)
        {
            // A finished task holds its place for good: swapping it out would free a place without counting a drop.
            return (checklist, null);
        }

        IReadOnlyCollection<Guid> focused = FocusSet(checklist);
        int inFocus = task.Subtasks.Count(step => !step.IsDone && focused.Contains(step.Id));
        if (slot.StepId is Guid stepId && inFocus > 1 && focused.Contains(stepId))
        {
            return (WithFocus(checklist, focused.Where(id => id != stepId)), null);
        }

        return (Cleaned(checklist.Remove(task.Id)), task.Id);
    }

    /// <summary>
    /// Fewer steps in focus until the checklist fits <paramref name="allowance"/>: the last focused steps of the last tasks go first,
    /// and every task keeps at least one. Tasks themselves are never taken off here (that is the pick).
    /// </summary>
    public static SessionChecklist FittedToCap(SessionChecklist checklist, int allowance)
    {
        var focused = FocusSet(checklist).ToList();
        SessionChecklist result = checklist;
        foreach (SessionTask task in checklist.Tasks.AsEnumerable().Reverse())
        {
            List<Guid> taskFocus = task.Subtasks.Where(step => !step.IsDone && focused.Contains(step.Id)).Select(step => step.Id).ToList();
            for (int index = taskFocus.Count - 1; index >= 1 && Used(result) > allowance; index--)
            {
                focused.Remove(taskFocus[index]);
                result = WithFocus(result, focused);
            }
        }

        return result;
    }

    /// <summary>
    /// The task the swapped place <paramref name="slot"/> leaves the newcomer in front of (Implementation Notes "Session slots: picking and
    /// order": the newcomer takes the swapped place's number): the task after the slot's task in the checklist's order; null when it was
    /// the last (the newcomer then stays at the end, which is that number too). Worked out before the swap.
    /// </summary>
    public static Guid? FollowerOf(SessionChecklist checklist, SessionSlot slot)
    {
        int at = checklist.Tasks.ToList().FindIndex(task => task.Id == slot.TaskId);
        return at >= 0 && at + 1 < checklist.Tasks.Count ? checklist.Tasks[at + 1].Id : null;
    }

    /// <summary>
    /// The task <paramref name="id"/> moved in front of <paramref name="beforeId"/> (at the end when null or gone), nothing else changed
    /// (no label taken from its neighbour, unlike a drag): the swap's order rule.
    /// </summary>
    public static SessionChecklist PlacedBefore(SessionChecklist checklist, Guid id, Guid? beforeId)
    {
        SessionTask? moved = checklist.Tasks.FirstOrDefault(task => task.Id == id);
        if (moved is null || beforeId == id)
        {
            return checklist;
        }

        List<SessionTask> rest = checklist.Tasks.Where(task => task.Id != id).ToList();
        int at = beforeId is Guid before ? rest.FindIndex(task => task.Id == before) : -1;
        rest.Insert(at < 0 ? rest.Count : at, moved);
        return rest.Select(task => task.Id).SequenceEqual(checklist.Tasks.Select(task => task.Id)) ? checklist : checklist with { Tasks = rest };
    }

    /// <summary>The checklist with <paramref name="focused"/> as its focused steps, keeping only open steps of open tasks (null for none).</summary>
    public static SessionChecklist WithFocus(SessionChecklist checklist, IEnumerable<Guid> focused)
    {
        var open = checklist.Tasks.Where(task => !task.IsDone)
            .SelectMany(task => task.Subtasks.Where(step => !step.IsDone).Select(step => step.Id))
            .ToHashSet();
        List<Guid> kept = focused.Distinct().Where(open.Contains).ToList();
        return checklist with { FocusedSteps = kept.Count == 0 ? null : kept };
    }

    /// <summary>The focused steps that are no longer open steps (finished, taken off) left out.</summary>
    public static SessionChecklist Cleaned(SessionChecklist checklist)
    {
        if (checklist.FocusedSteps is null)
        {
            return checklist;
        }

        SessionChecklist cleaned = WithFocus(checklist, checklist.FocusedSteps);
        return cleaned.FocusedSteps is { } kept && checklist.FocusedSteps.SequenceEqual(kept) ? checklist : cleaned;
    }

    private static IReadOnlyCollection<Guid> FocusSet(SessionChecklist checklist)
    {
        return (IReadOnlyCollection<Guid>?)checklist.FocusedSteps?.ToHashSet() ?? [];
    }

    // The open task holding the open step stepId; null when there is none.
    private static SessionTask? OpenStep(SessionChecklist checklist, Guid stepId)
    {
        return checklist.Tasks.FirstOrDefault(task => !task.IsDone && task.Subtasks.Any(step => step.Id == stepId && !step.IsDone));
    }
}
