using AccountabiliBuddy.App.Services.Reminders;
using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Goals;
using AccountabiliBuddy.Core.Tasks;
using Material.Icons;

namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>
/// Goal reminders on this PC (round 2 R5.5, brief §5.9, §5.10): a reminder source that rings through the task reminder engine
/// (<see cref="Tasks.TaskReminderService"/>, <see cref="Tasks.TaskReminderRinger"/>) without becoming a task. Suggestions only:
/// never Can't miss, never full screen, nothing locks or waits, nothing is written to a task, exported to Google Calendar or synced.
/// <list type="bullet">
/// <item>Each live reminder of a live goal rings at its local time on its days (<see cref="GoalReminderTimes"/>); a goal paused today
/// sends nothing. Only today's occurrences ring (a goal's day is the day): yesterday's only when snoozed into today.</item>
/// <item>"Skip when the goal is already done" is checked when the occurrence comes due: done then (today hit, or the period's target
/// met), it is marked skipped and never rings.</item>
/// <item>"Nudge when the week falls behind" (on any of the goal's reminders) rings once at 9:00 AM the morning after the goal drops below
/// pace (<see cref="GoalNudge"/>), while it is still behind.</item>
/// <item>ProductaBuddy mode rings once, quietly, as a plain notification (one bell, no volume raise); AccountabiliBuddy mode rings like
/// a normal task reminder (the bell repeats until the ring time runs out; the engine has no toast-then-overlay step for reminders).</item>
/// </list>
/// Snoozed, dismissed, skipped and rung-out occurrences are kept device-local in the goals store ("goals.reminderState", ids and times
/// only). Goal names and reasons reach the card only, never a log. Waits for the goal data's first load before anything comes due, so
/// "Skip when done" never reads an empty history. Use from the UI thread only.
/// </summary>
public sealed class GoalReminderSource : IReminderSource
{
    /// <summary>The entries' rule for a reminder's occurrence.</summary>
    public const string ReminderRule = "goal";

    /// <summary>The entries' rule for the morning-after nudge (its <see cref="TaskReminderEntry.ReminderId"/> is the goal's id).</summary>
    public const string NudgeRule = "nudge";

    private readonly GoalsService _goals;
    private readonly GoalSourceDataService _data;
    private readonly TimeProvider _time;

    public GoalReminderSource(GoalsService goals, GoalSourceDataService data, TimeProvider time)
    {
        _goals = goals;
        _data = data;
        _time = time;
        _goals.Store.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        _data.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public TaskReminderOrigin Origin => TaskReminderOrigin.Goal;

    public event EventHandler? Changed;

    /// <summary>Shows a goal on the Goals page (set by the app); true = with today's day panel open on Edit (Log a value / Add time).</summary>
    public Action<Guid, bool>? OpenGoal { get; set; }

    private TimeZoneInfo Zone => _time.LocalTimeZone;

    /// <summary>The app's mode changed (the nudge default and the quiet ring follow it).</summary>
    public void NotifyModeChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<TaskReminderEntry> Entries(DateTimeOffset now)
    {
        GoalStore store = _goals.Store;
        if (!store.IsReady)
        {
            return [];
        }

        DateOnly today = DayOf(now);
        DateTimeOffset dayStart = TaskReminderTimes.Local(today, TimeOnly.MinValue, Zone);
        GoalReminderState state = store.ReminderState;
        GoalReminderState updated = state;
        var facts = new Dictionary<Guid, GoalFacts>();
        var entries = new List<TaskReminderEntry>();

        foreach (GoalReminder reminder in store.Lists.Reminders.Where(item => item.IsLive))
        {
            if (LiveGoal(reminder.GoalId) is not Goal goal || goal.IsPausedOn(today))
            {
                continue;
            }

            foreach (DateOnly day in new[] { today.AddDays(-1), today })
            {
                if (GoalReminderTimes.On(reminder, day, Zone) is not DateTimeOffset due)
                {
                    continue;
                }

                string key = ReminderKey(reminder.Id, due);
                GoalReminderMark? mark = state.MarkOf(key);
                if (mark is { IsHandled: true })
                {
                    continue;
                }

                DateTimeOffset ringAt = mark?.SnoozedUntil ?? due;
                if (day < today && ringAt < dayStart)
                {
                    continue;
                }

                if (ringAt <= now)
                {
                    // Comes due now: nothing until the goal data is in (the skip rule reads it); then "Skip when done" at ring time.
                    if (!_data.IsLoaded)
                    {
                        continue;
                    }

                    if (reminder.SkipWhenDone && IsDone(FactsOf(goal, today, facts)))
                    {
                        updated = updated.With(key, new GoalReminderMark(GoalReminderMarkKind.Skipped, now), now);
                        continue;
                    }
                }

                entries.Add(Entry(goal.Id, reminder.Id, ReminderRule, due, ringAt, mark));
            }
        }

        if (_data.IsLoaded)
        {
            entries.AddRange(Nudges(store, state, today, dayStart, now));
        }

        if (!ReferenceEquals(updated, state))
        {
            _ = store.SetReminderStateAsync(updated);
        }

        return entries;
    }

    public Task SnoozeAsync(TaskReminderEntry entry, DateTimeOffset until)
    {
        DateTimeOffset now = _time.GetUtcNow();
        int count = (_goals.Store.ReminderState.MarkOf(MarkKey(entry))?.SnoozeCount ?? 0) + 1;
        return MarkAsync(entry, new GoalReminderMark(GoalReminderMarkKind.Snoozed, now, until, count));
    }

    public Task DismissAsync(TaskReminderEntry entry)
    {
        return MarkAsync(entry, new GoalReminderMark(GoalReminderMarkKind.Dismissed, _time.GetUtcNow()));
    }

    public Task RingTimedOutAsync(TaskReminderEntry entry)
    {
        return MarkAsync(entry, new GoalReminderMark(GoalReminderMarkKind.RangOut, _time.GetUtcNow()));
    }

    /// <summary>ProductaBuddy mode: a plain notification (one quiet bell, no volume raise; brief §5.10).</summary>
    public bool RingsQuietly(TaskReminderEntry entry)
    {
        return _goals.IsProductaBuddy;
    }

    public ReminderSourceCard? Card(TaskReminderEntry entry)
    {
        if (LiveGoal(entry.TaskId) is not Goal goal)
        {
            return null;
        }

        DateOnly today = DayOf(_time.GetUtcNow());
        GoalFacts facts = GoalFactsBuilder.Build(goal, _data.Current, today, _goals.Store.PageSettings.WeekStart);
        bool nudge = entry.Rule == NudgeRule;
        string head = nudge
            ? $"Goal nudge · behind {GoalTexts.PeriodName(goal.Target.Period)}"
            : $"Goal reminder · {GoalTexts.Time(entry.Due, Zone)}";
        if (entry.Status == TaskReminderStatus.Snoozed)
        {
            head += " · snoozed";
        }

        string reason = nudge
            ? "You dropped below pace yesterday. Just this one nudge."
            : _goals.Lists.Reminders.FirstOrDefault(item => item.Id == entry.ReminderId)?.Reason ?? string.Empty;
        (string primary, MaterialIconKind primaryIcon) = PrimaryOf(goal, facts);
        return new ReminderSourceCard(
            head,
            goal.Name,
            reason,
            $"{GoalNumbers.Number(facts)} · {StatusText(facts.Status)}",
            GoalIcons.Of(goal.Icon),
            GoalIcons.Brush(goal.Color),
            primary,
            primaryIcon,
            "Open goal");
    }

    /// <summary>
    /// The card's accent button, the kind's quick log: Tick today (a habit not hit yet: a hand entry of 1 for today, as the goal menu's
    /// Tick today), Log a value… / Add time… (the goal with today's day panel on Edit), else Open goal. Handled afterwards.
    /// </summary>
    public async Task PrimaryAsync(TaskReminderEntry entry)
    {
        if (LiveGoal(entry.TaskId) is Goal goal)
        {
            DateOnly today = DayOf(_time.GetUtcNow());
            GoalFacts facts = GoalFactsBuilder.Build(goal, _data.Current, today, _goals.Store.PageSettings.WeekStart);
            switch (QuickLogOf(goal, facts))
            {
                case QuickLog.Tick:
                    await _goals.SaveEntryAsync(new GoalEntry(Guid.NewGuid(), goal.Id, today, null, 1, GoalEntrySource.Hand, _time.GetUtcNow()), this);
                    break;
                case QuickLog.Log:
                    OpenGoal?.Invoke(goal.Id, true);
                    break;
                default:
                    OpenGoal?.Invoke(goal.Id, false);
                    break;
            }
        }

        await DismissAsync(entry);
    }

    public async Task OpenAsync(TaskReminderEntry entry)
    {
        OpenGoal?.Invoke(entry.TaskId, false);
        await DismissAsync(entry);
    }

    /// <summary>Whether a goal counts as done today for "Skip when the goal is already done": today hit, or the period's target met.</summary>
    public static bool IsDone(GoalFacts facts)
    {
        if (!facts.HasPeriodTarget)
        {
            return false;
        }

        return facts.TodayState.Hit || (facts.PeriodGoal.Target.Period != GoalPeriod.Day && facts.Period.Met);
    }

    // The occurrence's mark key: a reminder's by its id and due time; the nudge's by its goal and day.
    private string MarkKey(TaskReminderEntry entry)
    {
        return entry.Rule == NudgeRule ? NudgeKey(entry.TaskId, DayOf(entry.Due)) : ReminderKey(entry.ReminderId, entry.Due);
    }

    private static string ReminderKey(Guid reminderId, DateTimeOffset due) => $"{reminderId:N}:{due.UtcTicks}";

    private static string NudgeKey(Guid goalId, DateOnly day) => $"nudge:{goalId:N}:{day:yyyy-MM-dd}";

    private Task MarkAsync(TaskReminderEntry entry, GoalReminderMark mark)
    {
        GoalStore store = _goals.Store;
        return store.SetReminderStateAsync(store.ReminderState.With(MarkKey(entry), mark, mark.At));
    }

    // "Nudge when the week falls behind": today's 9:00 AM nudge for each goal that dropped below pace with yesterday (and yesterday's,
    // only when snoozed into today and the goal is still behind).
    private IEnumerable<TaskReminderEntry> Nudges(GoalStore store, GoalReminderState state, DateOnly today, DateTimeOffset dayStart, DateTimeOffset now)
    {
        HashSet<Guid> nudging = store.Lists.Reminders.Where(item => item.IsLive && item.NudgeWhenBehind).Select(item => item.GoalId).ToHashSet();
        GoalWeekStart weekStart = store.PageSettings.WeekStart;
        foreach (Guid goalId in nudging)
        {
            if (LiveGoal(goalId) is not Goal goal || goal.IsPausedOn(today))
            {
                continue;
            }

            string yesterdayKey = NudgeKey(goal.Id, today.AddDays(-1));
            if (state.MarkOf(yesterdayKey) is { Kind: GoalReminderMarkKind.Snoozed, SnoozedUntil: DateTimeOffset until } snoozed && until >= dayStart
                && GoalStatus.ThisPeriod(goal, _data.Current, today, weekStart) == GoalPeriodStatus.Behind)
            {
                yield return Entry(goal.Id, goal.Id, NudgeRule, GoalReminderTimes.NudgeOn(today.AddDays(-1), Zone), until, snoozed);
            }

            GoalReminderMark? mark = state.MarkOf(NudgeKey(goal.Id, today));
            if (mark is { IsHandled: true } || !GoalNudge.DroppedBehind(goal, _data.Current, today, weekStart))
            {
                continue;
            }

            DateTimeOffset due = GoalReminderTimes.NudgeOn(today, Zone);
            yield return Entry(goal.Id, goal.Id, NudgeRule, due, mark?.SnoozedUntil ?? due, mark);
        }
    }

    private static TaskReminderEntry Entry(Guid goalId, Guid reminderId, string rule, DateTimeOffset due, DateTimeOffset ringAt, GoalReminderMark? mark)
    {
        TaskReminderStatus status = mark?.Kind == GoalReminderMarkKind.Snoozed ? TaskReminderStatus.Snoozed : TaskReminderStatus.Up;
        return new TaskReminderEntry(goalId, reminderId, rule, due, status, ringAt, mark?.SnoozeCount ?? 0, CantMiss: false)
        {
            Origin = TaskReminderOrigin.Goal,
        };
    }

    // The live goal as the screens show it (Deep work with its targets as views); null when gone.
    private Goal? LiveGoal(Guid goalId)
    {
        Goal? stored = _goals.Lists.Goals.FirstOrDefault(goal => goal.Id == goalId && goal.IsLive);
        return stored is { IsDeepWork: true } ? _goals.DeepWork ?? stored : stored;
    }

    private GoalFacts FactsOf(Goal goal, DateOnly today, Dictionary<Guid, GoalFacts> cache)
    {
        if (!cache.TryGetValue(goal.Id, out GoalFacts? facts))
        {
            facts = GoalFactsBuilder.Build(goal, _data.Current, today, _goals.Store.PageSettings.WeekStart);
            cache[goal.Id] = facts;
        }

        return facts;
    }

    private DateOnly DayOf(DateTimeOffset at)
    {
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, Zone).DateTime);
    }

    private static (string Text, MaterialIconKind Icon) PrimaryOf(Goal goal, GoalFacts facts) => QuickLogOf(goal, facts) switch
    {
        QuickLog.Tick => ("Tick today", MaterialIconKind.Check),
        QuickLog.Log when goal.Kind == GoalKind.Time => ("Add time…", MaterialIconKind.TimerOutline),
        QuickLog.Log => ("Log a value…", MaterialIconKind.PencilOutline),
        _ => ("Open goal", MaterialIconKind.TrackChanges),
    };

    // The goal menu's quick log for the kind (Deep work counts focus only: it opens).
    private static QuickLog QuickLogOf(Goal goal, GoalFacts facts)
    {
        if (goal.IsDeepWork)
        {
            return QuickLog.Open;
        }

        return goal.Kind switch
        {
            GoalKind.Habit when !facts.TodayState.Hit => QuickLog.Tick,
            GoalKind.Amount or GoalKind.Time => QuickLog.Log,
            _ => QuickLog.Open,
        };
    }

    private static string StatusText(GoalPeriodStatus status) => status switch
    {
        GoalPeriodStatus.OnTrack => "On track",
        GoalPeriodStatus.Behind => "Behind",
        GoalPeriodStatus.Paused => "Paused",
        _ => "Not due yet",
    };

    private enum QuickLog
    {
        Tick,
        Log,
        Open,
    }
}
