using System.Text.Json;
using AccountabiliBuddy.Core.Goals;

namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>
/// The goals' rules on this PC (brief §5.3, §5.7, §5.10; plan R4.6), over <see cref="GoalStore"/>, for the Goals screens (R5):
/// <list type="bullet">
/// <item><b>Deep work's daily target</b> is the "goal" document: read from and written through <see cref="GoalService.SetAsync"/> (ADR 29
/// untouched; 30 to 480 minutes, as the goal card). <b>Its weekly target</b> and <b>"Track time only"</b> live in "goals.deepWork".</item>
/// <item><b>Lowering Deep work</b> (daily, weekly, or Track time only) follows <see cref="GoalLowering"/> with the pending-change pattern
/// of <see cref="Tasks.TaskSettingsService"/>: outside ProductaBuddy mode it is refused during a session and otherwise waits the
/// looser-change wait (counted by the trusted clock, kept in the document so a restart doesn't skip it), then applies only with no
/// session running and the existing work-hours rule clear (the goal card's: no lowering while the work-hours guard has work left
/// today); in ProductaBuddy mode it skips the wait and the session rule but keeps the work-hours rule (Q362). Raising applies at
/// once and drops a waiting lower value; a waiting change can be cancelled. Switching to AccountabiliBuddy mode restarts the wait.</item>
/// <item><b>Deep work</b> can't be paused, deleted or logged by hand, and its sources stay "all counted focus" (plan §3b 6).</item>
/// <item><b>Every other goal</b> feeds no guard: target changes, pausing and deleting apply at once in both modes.</item>
/// </list>
/// Guards, the goal unlock and the phone's focusDay read counted focus (<see cref="DeepWorkService"/>) and the "goal" document only,
/// never goal counts or entries. Use from the UI thread only.
/// </summary>
public sealed class GoalsService
{
    public const int MinDailyMinutes = 30;
    public const int MaxDailyMinutes = 480;
    public const int MinWeeklyMinutes = 30;
    public const int MaxWeeklyMinutes = MaxDailyMinutes * 7;

    private readonly GoalStore _store;
    private readonly GoalService _daily;
    private readonly SessionController _sessions;
    private readonly TimeProvider _time;
    private readonly Func<bool> _isProductaBuddy;
    private readonly Func<bool> _lowerWaitsNow;
    private readonly Func<TimeSpan> _looserWait;
    private bool _writingDaily;
    private bool _applying;
    private bool _wasProductaBuddy;

    /// <param name="daily">The "goal" document: Deep work's daily target.</param>
    /// <param name="isProductaBuddy">The app is in ProductaBuddy mode (ADR 68).</param>
    /// <param name="lowerWaitsNow">The existing daily-goal rule: the work-hours guard has work left today.</param>
    /// <param name="looserWait">The looser-change wait of the configuration in effect.</param>
    public GoalsService(
        GoalStore store, GoalService daily, SessionController sessions, TimeProvider time,
        Func<bool> isProductaBuddy, Func<bool> lowerWaitsNow, Func<TimeSpan> looserWait)
    {
        _store = store;
        _daily = daily;
        _sessions = sessions;
        _time = time;
        _isProductaBuddy = isProductaBuddy;
        _lowerWaitsNow = lowerWaitsNow;
        _looserWait = looserWait;
        _wasProductaBuddy = isProductaBuddy();
        _sessions.Ticked += (_, _) => _ = ApplyPendingIfReadyAsync();
        _daily.Changed += (_, _) => _ = DailyChangedElsewhereAsync();
    }

    public GoalStore Store => _store;

    public GoalModelState State => _store.State;

    public GoalLists Lists => _store.Lists;

    /// <summary>Deep work's own values (weekly target, Track time only, the waiting changes).</summary>
    public DeepWorkGoalSettings DeepWorkSettings => _store.DeepWork;

    /// <summary>
    /// The built-in Deep work goal as the screens show it: its stored record with the daily target from the "goal" document and the
    /// weekly one from "goals.deepWork", each with its waiting change; no targets with Track time only. Null until the goals are ready.
    /// </summary>
    public Goal? DeepWork
    {
        get
        {
            if (_store.Lists.DeepWorkRecord is not Goal record)
            {
                return null;
            }

            DeepWorkGoalSettings settings = _store.DeepWork;
            double? daily = settings.TrackTimeOnly ? null : _daily.Current.DailyMinutes;
            var dailyTarget = new GoalTarget(GoalPeriod.Day, daily, GoalUnit.Minutes, PendingChange: settings.PendingTrackTimeOnly ?? settings.PendingDaily);
            GoalPendingChange? weeklyPending = settings.PendingTrackTimeOnly ?? settings.PendingWeekly;
            double? weekly = settings.TrackTimeOnly ? null : settings.WeeklyMinutes;
            GoalTarget? weeklyTarget = weekly is null && weeklyPending is null
                ? null
                : new GoalTarget(GoalPeriod.Week, weekly, GoalUnit.Minutes, PendingChange: weeklyPending);
            return record with { Target = dailyTarget, SecondTarget = weeklyTarget };
        }
    }

    private DateTimeOffset Now => _time.GetUtcNow();

    private DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Now, TimeZoneInfo.Local).DateTime);

    private bool SessionRunning => _sessions.State.IsActive;

    // ---- Deep work ----

    /// <summary>Deep work's daily target (the "goal" document). Raising applies at once; lowering follows the Deep work rule.</summary>
    public async Task<GoalChangeResult> SetDeepWorkDailyAsync(int minutes)
    {
        if (!_store.IsReady || _store.Lists.DeepWorkRecord is not Goal record)
        {
            return GoalChangeResult.NotAvailable;
        }

        int wanted = Math.Clamp(minutes, MinDailyMinutes, MaxDailyMinutes);
        DeepWorkGoalSettings settings = _store.DeepWork;
        // Setting a daily target ends Track time only: adding a target is stricter, at once.
        if (settings.TrackTimeOnly || settings.PendingTrackTimeOnly is not null)
        {
            settings = settings with { TrackTimeOnly = false, PendingTrackTimeOnly = null };
            await _store.SetDeepWorkAsync(settings);
        }

        // The guards read the "goal" document's minutes, so lower or not is judged against them.
        var current = new GoalTarget(GoalPeriod.Day, _daily.Current.DailyMinutes, GoalUnit.Minutes);
        GoalChangeDecision decision = GoalLowering.Decide(record, current, wanted, _isProductaBuddy(), _lowerWaitsNow(), Now, _looserWait());
        switch (decision.Outcome)
        {
            case GoalChangeOutcome.Unchanged:
                await _store.SetDeepWorkAsync(settings with { PendingDaily = null });
                return GoalChangeResult.Unchanged;
            case GoalChangeOutcome.ApplyNow:
                await WriteDailyAsync(wanted);
                await _store.SetDeepWorkAsync(_store.DeepWork with { PendingDaily = null });
                return GoalChangeResult.Applied;
            default:
                return await KeepWaitingAsync(settings, settings.PendingDaily, decision.Pending(wanted)!, pending => settings with { PendingDaily = pending });
        }
    }

    /// <summary>Deep work's weekly target in minutes (null = none). Raising or adding applies at once; lowering or removing follows the Deep work rule.</summary>
    public async Task<GoalChangeResult> SetDeepWorkWeeklyAsync(int? minutes)
    {
        if (!_store.IsReady || _store.Lists.DeepWorkRecord is not Goal record)
        {
            return GoalChangeResult.NotAvailable;
        }

        int? wanted = minutes is int value ? Math.Clamp(value, MinWeeklyMinutes, MaxWeeklyMinutes) : null;
        DeepWorkGoalSettings settings = _store.DeepWork;
        if (wanted is not null && (settings.TrackTimeOnly || settings.PendingTrackTimeOnly is not null))
        {
            // A weekly target ends Track time only: stricter, at once.
            settings = settings with { TrackTimeOnly = false, PendingTrackTimeOnly = null };
            await _store.SetDeepWorkAsync(settings);
        }

        var current = new GoalTarget(GoalPeriod.Week, settings.TrackTimeOnly ? null : settings.WeeklyMinutes, GoalUnit.Minutes);
        GoalChangeDecision decision = GoalLowering.Decide(record, current, wanted, _isProductaBuddy(), _lowerWaitsNow(), Now, _looserWait());
        switch (decision.Outcome)
        {
            case GoalChangeOutcome.Unchanged:
                await _store.SetDeepWorkAsync(settings with { PendingWeekly = null });
                return GoalChangeResult.Unchanged;
            case GoalChangeOutcome.ApplyNow:
                await _store.SetDeepWorkAsync(settings with { WeeklyMinutes = wanted, PendingWeekly = null });
                return GoalChangeResult.Applied;
            default:
                return await KeepWaitingAsync(settings, settings.PendingWeekly, decision.Pending(wanted)!, pending => settings with { PendingWeekly = pending });
        }
    }

    /// <summary>
    /// "Track time only, no target" (on) removes Deep work's daily and weekly targets on the Goals screens: a lowering, so it follows the
    /// Deep work rule. Off brings the daily target back (stricter, at once; the weekly one stays none). The guards keep the "goal"
    /// document's minutes either way (Q374).
    /// </summary>
    public async Task<GoalChangeResult> SetTrackTimeOnlyAsync(bool on)
    {
        if (!_store.IsReady || _store.Lists.DeepWorkRecord is not Goal record)
        {
            return GoalChangeResult.NotAvailable;
        }

        DeepWorkGoalSettings settings = _store.DeepWork;
        if (!on)
        {
            if (!settings.TrackTimeOnly && settings.PendingTrackTimeOnly is null)
            {
                return GoalChangeResult.Unchanged;
            }

            await _store.SetDeepWorkAsync(settings with { TrackTimeOnly = false, PendingTrackTimeOnly = null });
            return GoalChangeResult.Applied;
        }

        if (settings.TrackTimeOnly)
        {
            return GoalChangeResult.Unchanged;
        }

        var current = new GoalTarget(GoalPeriod.Day, _daily.Current.DailyMinutes, GoalUnit.Minutes);
        GoalChangeDecision decision = GoalLowering.Decide(record, current, null, _isProductaBuddy(), _lowerWaitsNow(), Now, _looserWait());
        if (decision.Outcome == GoalChangeOutcome.ApplyNow)
        {
            await _store.SetDeepWorkAsync(TrackingTimeOnly(settings));
            return GoalChangeResult.Applied;
        }

        return await KeepWaitingAsync(settings, settings.PendingTrackTimeOnly, decision.Pending(null)!, pending => settings with { PendingTrackTimeOnly = pending });
    }

    /// <summary>Cancel on a waiting Deep work change (the Target tile, Edit goal).</summary>
    public async Task CancelDeepWorkPendingAsync(DeepWorkTargetKind kind)
    {
        if (!_store.IsReady)
        {
            return;
        }

        DeepWorkGoalSettings settings = _store.DeepWork;
        await _store.SetDeepWorkAsync(kind switch
        {
            DeepWorkTargetKind.Daily => settings with { PendingDaily = null },
            DeepWorkTargetKind.Weekly => settings with { PendingWeekly = null },
            _ => settings with { PendingTrackTimeOnly = null },
        });
    }

    /// <summary>
    /// Waiting Deep work changes that may apply now do (<see cref="GoalLowering.CanApply"/>): their time has come, no session runs
    /// (outside ProductaBuddy mode) and the work-hours rule doesn't hold them. Runs on every session tick.
    /// </summary>
    public async Task ApplyPendingIfReadyAsync()
    {
        if (!_store.IsReady || _applying || !_store.DeepWork.HasPending)
        {
            return;
        }

        _applying = true;
        try
        {
            DateTimeOffset now = Now;
            bool productaBuddy = _isProductaBuddy();
            bool session = SessionRunning;
            bool lowerWaits = _lowerWaitsNow();
            bool Ready(GoalPendingChange? pending) => pending is not null && GoalLowering.CanApply(pending, now, session, productaBuddy, lowerWaits);

            DeepWorkGoalSettings settings = _store.DeepWork;
            if (!productaBuddy)
            {
                // A change made in ProductaBuddy mode seen in AccountabiliBuddy mode (a switch this service didn't see, e.g. while the
                // app was closed) waits the full delay from now first (GoalLowering.IntoAccountabiliBuddy).
                GoalPendingChange? Restarted(GoalPendingChange? pending) => pending is null ? null : GoalLowering.IntoAccountabiliBuddy(pending, now, _looserWait());
                settings = settings with
                {
                    PendingDaily = Restarted(settings.PendingDaily),
                    PendingWeekly = Restarted(settings.PendingWeekly),
                    PendingTrackTimeOnly = Restarted(settings.PendingTrackTimeOnly),
                };
            }

            if (Ready(settings.PendingTrackTimeOnly))
            {
                settings = TrackingTimeOnly(settings);
            }

            if (Ready(settings.PendingWeekly))
            {
                settings = settings with { WeeklyMinutes = settings.PendingWeekly!.Amount is double weekly ? (int?)(int)weekly : null, PendingWeekly = null };
            }

            if (Ready(settings.PendingDaily))
            {
                // Still a lowering of what the guards read (a later raise elsewhere drops it before this; checked again here).
                if (settings.PendingDaily!.Amount is double daily && daily < _daily.Current.DailyMinutes)
                {
                    await WriteDailyAsync((int)daily);
                }

                settings = settings with { PendingDaily = null };
            }

            await _store.SetDeepWorkAsync(settings);
        }
        finally
        {
            _applying = false;
        }
    }

    /// <summary>
    /// The mode changed (ADR 68). Into AccountabiliBuddy mode, a change still waiting (made in ProductaBuddy mode, where it only waited
    /// for the work-hours rule) waits the full looser-change wait from now, so the switch never makes a lowering land sooner.
    /// </summary>
    public async Task ModeChangedAsync()
    {
        bool productaBuddy = _isProductaBuddy();
        bool intoGuarding = _wasProductaBuddy && !productaBuddy;
        _wasProductaBuddy = productaBuddy;
        if (!intoGuarding || !_store.IsReady || !_store.DeepWork.HasPending)
        {
            return;
        }

        DateTimeOffset earliest = Now + _looserWait();
        GoalPendingChange? Later(GoalPendingChange? pending) =>
            pending is not null && (pending.AppliesAt < earliest || pending.MadeInProductaBuddy)
                ? pending with { AppliesAt = pending.AppliesAt < earliest ? earliest : pending.AppliesAt, MadeInProductaBuddy = false }
                : pending;
        DeepWorkGoalSettings settings = _store.DeepWork;
        await _store.SetDeepWorkAsync(settings with
        {
            PendingDaily = Later(settings.PendingDaily),
            PendingWeekly = Later(settings.PendingWeekly),
            PendingTrackTimeOnly = Later(settings.PendingTrackTimeOnly),
        });
    }

    /// <summary>
    /// "goals.deepWork" from a backup's device settings (ADR 71; plan §3b 6), through the same rules as a change made here: a lower weekly
    /// target, no weekly target or Track time only waits (or is refused during a session); a raise applies at once; a waiting change in
    /// the backup is never taken over. Returns a note for the restore summary, or null.
    /// </summary>
    public async Task<string?> RestoreDeepWorkAsync(JsonElement value)
    {
        if (!_store.IsReady)
        {
            return "Deep work's weekly target wasn't restored: this PC's goals aren't ready yet.";
        }

        DeepWorkGoalSettings? wanted;
        try
        {
            wanted = value.Deserialize<DeepWorkGoalSettings>(GoalStore.DocumentJson);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            wanted = null;
        }

        if (wanted is null)
        {
            return "Deep work's weekly target in this backup couldn't be read; this PC's was kept.";
        }

        var results = new List<GoalChangeResult>();
        if (wanted.TrackTimeOnly)
        {
            results.Add(await SetTrackTimeOnlyAsync(true));
        }
        else
        {
            results.Add(await SetTrackTimeOnlyAsync(false));
            results.Add(await SetDeepWorkWeeklyAsync(wanted.WeeklyMinutes));
        }

        if (results.Contains(GoalChangeResult.RefusedDuringSession))
        {
            return "Deep work's weekly target wasn't restored during a session (it would loosen it).";
        }

        return results.Contains(GoalChangeResult.Waiting)
            ? "Deep work's lower weekly target (or Track time only) from the backup waits the looser-change countdown and never starts during a session."
            : null;
    }

    // ---- Other goals, areas, entries, reminders, reviews (apply at once: they feed no guard) ----

    /// <summary>A new goal (never built in). Its area must be live and its sources choosable now (not "Coming soon").</summary>
    public async Task<GoalChangeResult> AddGoalAsync(Goal goal, object? source = null)
    {
        if (!_store.IsReady)
        {
            return GoalChangeResult.NotAvailable;
        }

        if (goal.BuiltIn || goal.Id == Goal.DeepWorkId || _store.Lists.Goals.Any(item => item.Id == goal.Id)
            || !IsLiveArea(goal.AreaId) || !goal.Sources.All(GoalDefaults.IsChoosable))
        {
            return GoalChangeResult.NotAllowed;
        }

        DateTimeOffset now = Now;
        Goal added = goal with
        {
            CreatedAt = goal.CreatedAt == default ? now : goal.CreatedAt,
            EditedAt = now,
            Pauses = null,
            DeletedAt = null,
            SecondTarget = null,
            Target = goal.Target with { PendingChange = null },
        };
        await _store.ChangeAsync(source, (lists, _) => lists with { Goals = [.. lists.Goals, added] });
        return GoalChangeResult.Applied;
    }

    /// <summary>
    /// An edit of a goal. Any other goal's changes (target up or down, sources, area) apply at once; its pauses go through
    /// <see cref="PauseGoalAsync"/> / <see cref="ResumeGoalAsync"/>. On Deep work only the name, icon, colour, area, place, Show on
    /// Today, extra tiles and notes are taken (its targets go through the Deep work methods); other sources, a kind or a pause are refused.
    /// </summary>
    public async Task<GoalChangeResult> UpdateGoalAsync(Goal goal, object? source = null)
    {
        if (!_store.IsReady)
        {
            return GoalChangeResult.NotAvailable;
        }

        Goal? stored = _store.Lists.Goals.FirstOrDefault(item => item.Id == goal.Id && item.IsLive);
        if (stored is null || !IsLiveArea(goal.AreaId))
        {
            return GoalChangeResult.NotAllowed;
        }

        Goal updated;
        if (stored.BuiltIn)
        {
            bool sameSources = goal.Sources.Count == 1 && goal.Sources[0].Type == GoalSourceType.FocusAll;
            if (!sameSources || goal.Kind != stored.Kind || !goal.BuiltIn || goal.DeletedAt is not null || (goal.Pauses ?? []).Count > 0)
            {
                return GoalChangeResult.NotAllowed;
            }

            updated = stored with
            {
                Name = goal.Name,
                Icon = goal.Icon,
                Color = goal.Color,
                AreaId = goal.AreaId,
                Position = goal.Position,
                ShowOnToday = goal.ShowOnToday,
                ExtraTiles = goal.ExtraTiles,
                Notes = goal.Notes,
            };
        }
        else
        {
            if (goal.BuiltIn || !goal.Sources.All(GoalDefaults.IsChoosable))
            {
                return GoalChangeResult.NotAllowed;
            }

            // Other goals feed no guard: GoalLowering applies their changes at once, so no waiting change is kept.
            updated = goal with
            {
                CreatedAt = stored.CreatedAt,
                Pauses = stored.Pauses,
                DeletedAt = null,
                SecondTarget = null,
                Target = goal.Target with { PendingChange = null },
            };
        }

        if (SameGoal(updated, stored))
        {
            return GoalChangeResult.Unchanged;
        }

        DateTimeOffset now = Now;
        await _store.ChangeAsync(source, (lists, _) => lists with { Goals = Replace(lists.Goals, item => item.Id == goal.Id, updated with { EditedAt = now }) });
        return GoalChangeResult.Applied;
    }

    /// <summary>Pause a goal from today (at once; brief §5.7). Deep work can't be paused.</summary>
    public async Task<GoalChangeResult> PauseGoalAsync(Guid goalId, object? source = null)
    {
        if (!_store.IsReady)
        {
            return GoalChangeResult.NotAvailable;
        }

        Goal? goal = _store.Lists.Goals.FirstOrDefault(item => item.Id == goalId && item.IsLive);
        if (goal is null || !GoalLowering.CanPauseOrDelete(goal))
        {
            return GoalChangeResult.NotAllowed;
        }

        DateOnly today = Today;
        if (goal.IsPausedOn(today))
        {
            return GoalChangeResult.Unchanged;
        }

        Goal paused = goal with { Pauses = [.. goal.Pauses ?? [], new GoalPauseStretch(today)], EditedAt = Now };
        await _store.ChangeAsync(source, (lists, _) => lists with { Goals = Replace(lists.Goals, item => item.Id == goalId, paused) });
        return GoalChangeResult.Applied;
    }

    /// <summary>Resume a paused goal: today counts again (Q369). A stretch begun today is dropped.</summary>
    public async Task<GoalChangeResult> ResumeGoalAsync(Guid goalId, object? source = null)
    {
        if (!_store.IsReady)
        {
            return GoalChangeResult.NotAvailable;
        }

        Goal? goal = _store.Lists.Goals.FirstOrDefault(item => item.Id == goalId && item.IsLive);
        if (goal is null)
        {
            return GoalChangeResult.NotAllowed;
        }

        DateOnly today = Today;
        List<GoalPauseStretch> stretches = (goal.Pauses ?? []).ToList();
        int open = stretches.FindIndex(stretch => stretch.ResumedOn is null);
        if (open < 0)
        {
            return GoalChangeResult.Unchanged;
        }

        if (stretches[open].From >= today)
        {
            stretches.RemoveAt(open);
        }
        else
        {
            stretches[open] = stretches[open] with { ResumedOn = today };
        }

        Goal resumed = goal with { Pauses = stretches.Count == 0 ? null : stretches, EditedAt = Now };
        await _store.ChangeAsync(source, (lists, _) => lists with { Goals = Replace(lists.Goals, item => item.Id == goalId, resumed) });
        return GoalChangeResult.Applied;
    }

    /// <summary>Delete a goal (at once): a tombstone with no text, its entries and reminders with it. Deep work can't be deleted.</summary>
    public async Task<GoalChangeResult> DeleteGoalAsync(Guid goalId, object? source = null)
    {
        if (!_store.IsReady)
        {
            return GoalChangeResult.NotAvailable;
        }

        Goal? goal = _store.Lists.Goals.FirstOrDefault(item => item.Id == goalId && item.IsLive);
        if (goal is null || !GoalLowering.CanPauseOrDelete(goal))
        {
            return GoalChangeResult.NotAllowed;
        }

        await _store.ChangeAsync(source, (lists, now) => lists with
        {
            Goals = Replace(lists.Goals, item => item.Id == goalId, goal with { Name = string.Empty, Notes = null, DeletedAt = now, EditedAt = now }),
            Entries = lists.Entries.Select(entry => entry.GoalId == goalId && entry.IsLive ? entry with { DeletedAt = now, EditedAt = now } : entry).ToList(),
            Reminders = lists.Reminders.Select(reminder => reminder.GoalId == goalId && reminder.IsLive
                ? reminder with { Reason = null, DeletedAt = now, EditedAt = now }
                : reminder).ToList(),
        });
        return GoalChangeResult.Applied;
    }

    /// <summary>Add or edit an area (at once).</summary>
    public async Task<GoalChangeResult> SaveAreaAsync(GoalArea area, object? source = null)
    {
        if (!_store.IsReady)
        {
            return GoalChangeResult.NotAvailable;
        }

        if (_store.Lists.Areas.FirstOrDefault(item => item.Id == area.Id) is { IsLive: false })
        {
            return GoalChangeResult.NotAllowed;
        }

        GoalArea saved = area with { DeletedAt = null, EditedAt = Now };
        await _store.ChangeAsync(source, (lists, _) => lists with { Areas = Upsert(lists.Areas, item => item.Id == area.Id, saved) });
        return GoalChangeResult.Applied;
    }

    /// <summary>Delete an empty area (at once); one that still holds a goal is refused (move its goals first).</summary>
    public async Task<GoalChangeResult> DeleteAreaAsync(Guid areaId, object? source = null)
    {
        if (!_store.IsReady)
        {
            return GoalChangeResult.NotAvailable;
        }

        GoalArea? area = _store.Lists.Areas.FirstOrDefault(item => item.Id == areaId && item.IsLive);
        if (area is null || _store.Lists.Goals.Any(goal => goal.IsLive && goal.AreaId == areaId))
        {
            return GoalChangeResult.NotAllowed;
        }

        await _store.ChangeAsync(source, (lists, now) => lists with
        {
            Areas = Replace(lists.Areas, item => item.Id == areaId, area with { Name = string.Empty, DeletedAt = now, EditedAt = now }),
        });
        return GoalChangeResult.Applied;
    }

    /// <summary>
    /// A tick, a logged value or time added by hand (or its edit), at once. Never on Deep work (its source is counted focus only; time
    /// added by hand there is a hand-added session in History, ADR 20). Entries count toward goals only, never a guard.
    /// </summary>
    public async Task<GoalChangeResult> SaveEntryAsync(GoalEntry entry, object? source = null)
    {
        if (!_store.IsReady)
        {
            return GoalChangeResult.NotAvailable;
        }

        Goal? goal = _store.Lists.Goals.FirstOrDefault(item => item.Id == entry.GoalId && item.IsLive);
        if (goal is null || goal.BuiltIn || _store.Lists.Entries.FirstOrDefault(item => item.Id == entry.Id) is { IsLive: false })
        {
            return GoalChangeResult.NotAllowed;
        }

        GoalEntry saved = entry with { DeletedAt = null, EditedAt = Now };
        await _store.ChangeAsync(source, (lists, _) => lists with { Entries = Upsert(lists.Entries, item => item.Id == entry.Id, saved) });
        return GoalChangeResult.Applied;
    }

    public async Task<GoalChangeResult> DeleteEntryAsync(Guid entryId, object? source = null)
    {
        if (!_store.IsReady)
        {
            return GoalChangeResult.NotAvailable;
        }

        GoalEntry? entry = _store.Lists.Entries.FirstOrDefault(item => item.Id == entryId && item.IsLive);
        if (entry is null)
        {
            return GoalChangeResult.NotAllowed;
        }

        await _store.ChangeAsync(source, (lists, now) => lists with { Entries = Replace(lists.Entries, item => item.Id == entryId, entry with { DeletedAt = now, EditedAt = now }) });
        return GoalChangeResult.Applied;
    }

    /// <summary>Add or edit a goal reminder (a suggestion: at once, brief §5.9). Deep work may have reminders.</summary>
    public async Task<GoalChangeResult> SaveReminderAsync(GoalReminder reminder, object? source = null)
    {
        if (!_store.IsReady)
        {
            return GoalChangeResult.NotAvailable;
        }

        if (!_store.Lists.Goals.Any(goal => goal.Id == reminder.GoalId && goal.IsLive)
            || _store.Lists.Reminders.FirstOrDefault(item => item.Id == reminder.Id) is { IsLive: false })
        {
            return GoalChangeResult.NotAllowed;
        }

        GoalReminder saved = reminder with { DeletedAt = null, EditedAt = Now };
        await _store.ChangeAsync(source, (lists, _) => lists with { Reminders = Upsert(lists.Reminders, item => item.Id == reminder.Id, saved) });
        return GoalChangeResult.Applied;
    }

    public async Task<GoalChangeResult> DeleteReminderAsync(Guid reminderId, object? source = null)
    {
        if (!_store.IsReady)
        {
            return GoalChangeResult.NotAvailable;
        }

        GoalReminder? reminder = _store.Lists.Reminders.FirstOrDefault(item => item.Id == reminderId && item.IsLive);
        if (reminder is null)
        {
            return GoalChangeResult.NotAllowed;
        }

        await _store.ChangeAsync(source, (lists, now) => lists with
        {
            Reminders = Replace(lists.Reminders, item => item.Id == reminderId, reminder with { Reason = null, DeletedAt = now, EditedAt = now }),
        });
        return GoalChangeResult.Applied;
    }

    /// <summary>Add or edit a weekly review record (at once; "Adjust the target" goes through the goal's own change).</summary>
    public async Task<GoalChangeResult> SaveReviewAsync(GoalReviewRecord review, object? source = null)
    {
        if (!_store.IsReady)
        {
            return GoalChangeResult.NotAvailable;
        }

        if (_store.Lists.Reviews.FirstOrDefault(item => item.Id == review.Id) is { IsLive: false })
        {
            return GoalChangeResult.NotAllowed;
        }

        GoalReviewRecord saved = review with { DeletedAt = null, EditedAt = Now };
        await _store.ChangeAsync(source, (lists, _) => lists with { Reviews = Upsert(lists.Reviews, item => item.Id == review.Id, saved) });
        return GoalChangeResult.Applied;
    }

    /// <summary>The page's UI state (display only: at once).</summary>
    public Task SetUiPreferencesAsync(GoalUiPreferences preferences, object? source = null) => _store.SetUiAsync(preferences, source);

    /// <summary>Options › Charts (week start, heatmap labels, extra tiles; at once in both modes).</summary>
    public Task SetPageSettingsAsync(GoalPageSettings settings, object? source = null) => _store.SetPageSettingsAsync(settings, source);

    // ---- helpers ----

    private async Task<GoalChangeResult> KeepWaitingAsync(
        DeepWorkGoalSettings settings, GoalPendingChange? held, GoalPendingChange wanted, Func<GoalPendingChange, DeepWorkGoalSettings> with)
    {
        if (!_isProductaBuddy() && SessionRunning)
        {
            return GoalChangeResult.RefusedDuringSession;
        }

        // A wait already running for the same value keeps its time.
        if (held is not null && Nullable.Equals(held.Amount, wanted.Amount))
        {
            return GoalChangeResult.Waiting;
        }

        await _store.SetDeepWorkAsync(with(wanted));
        return GoalChangeResult.Waiting;
    }

    private static DeepWorkGoalSettings TrackingTimeOnly(DeepWorkGoalSettings settings)
    {
        return settings with { TrackTimeOnly = true, WeeklyMinutes = null, PendingTrackTimeOnly = null, PendingWeekly = null, PendingDaily = null };
    }

    private async Task WriteDailyAsync(int minutes)
    {
        _writingDaily = true;
        try
        {
            await _daily.SetAsync(minutes);
        }
        finally
        {
            _writingDaily = false;
        }
    }

    // The daily goal was set somewhere else (the goal card, the start-of-day card, another PC, a restore): that later choice wins, so a
    // waiting lower value from here is dropped rather than landing over it.
    private async Task DailyChangedElsewhereAsync()
    {
        if (_writingDaily || !_store.IsReady || _store.DeepWork.PendingDaily is null)
        {
            return;
        }

        await _store.SetDeepWorkAsync(_store.DeepWork with { PendingDaily = null });
    }

    private static bool SameGoal(Goal left, Goal right)
    {
        return JsonSerializer.Serialize(left with { EditedAt = default }, Core.Sync.SyncJson.Options)
            == JsonSerializer.Serialize(right with { EditedAt = default }, Core.Sync.SyncJson.Options);
    }

    private bool IsLiveArea(Guid areaId) => _store.Lists.Areas.Any(area => area.IsLive && area.Id == areaId);

    private static List<T> Replace<T>(IReadOnlyList<T> items, Func<T, bool> match, T replacement)
    {
        return items.Select(item => match(item) ? replacement : item).ToList();
    }

    private static List<T> Upsert<T>(IReadOnlyList<T> items, Func<T, bool> match, T replacement)
    {
        List<T> replaced = Replace(items, match, replacement);
        if (!items.Any(match))
        {
            replaced.Add(replacement);
        }

        return replaced;
    }
}
