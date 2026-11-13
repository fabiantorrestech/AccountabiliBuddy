using System.Text.Json;
using AccountabiliBuddy.Core.Goals;
using AccountabiliBuddy.Core.Sync;

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
/// <b>ADR 79 (GS3a):</b> the goals travel between the user's devices (<see cref="Sync.GoalListSync"/>). Deep work's weekly target and
/// "Track time only" travel in its record with their own stamp (<see cref="DeepWorkGoalSettings.Stamp"/>: this PC's EFFECTIVE values,
/// never a waiting one); one received from another device goes through the same rule as a change made here (<see cref="ApplySyncedAsync"/>):
/// higher or new at once (also during a session); lower, removed or "Track time only" turning on waits THIS PC's looser-change delay from
/// the moment it arrived here (the trusted clock), is held while a focus session the user started runs (outside ProductaBuddy mode) and
/// counts its wait from that session's end, keeps the work-hours rule, and lands with the sender's stamp, so nothing echoes. Cancel on such
/// a change stamps this PC's value again, which the sender then takes as a raise.
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
    // Every Deep work change runs alone: a raise asked for while a lowering applies is never lost under it (review fix).
    private readonly SemaphoreSlim _deepWorkGate = new(1, 1);
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
        _daily.Changed += (_, _) => DailyChangedElsewhere();
    }

    public GoalStore Store => _store;

    /// <summary>This device's sync id (ADR 79), the device Deep work's weekly target changed here is stamped with; the empty id before this PC is in a group. Set by the app.</summary>
    public Func<Guid> Self { get; set; } = () => Guid.Empty;

    /// <summary>
    /// ADR 79: Deep work's weekly target and "Track time only" as this PC sends them (<see cref="GoalDeepWorkSync.Compose"/>): the values
    /// in effect (never a waiting one) with their own stamp. Null until the goals are ready.
    /// </summary>
    public GoalDeepWorkTargets? DeepWorkTargetsForSync =>
        _store.IsReady && _store.Lists.DeepWorkRecord is Goal record
            ? new GoalDeepWorkTargets(_store.DeepWork.EffectiveWeekly, _store.DeepWork.TrackTimeOnly, OwnStamp(_store.DeepWork, record))
            : null;

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

    /// <summary>A looser Deep work change would be refused now: a session runs outside ProductaBuddy mode (the screens say so first).</summary>
    public bool LooserChangeRefusedNow => !_isProductaBuddy() && SessionRunning;

    /// <summary>Whether the app is in ProductaBuddy mode (the screens' wording).</summary>
    public bool IsProductaBuddy => _isProductaBuddy();

    /// <summary>The looser-change wait in effect ("waits 10 min").</summary>
    public TimeSpan LooserWait => _looserWait();

    private DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Now, TimeZoneInfo.Local).DateTime);

    private bool SessionRunning => _sessions.State.IsActive;

    // ---- Deep work (one change at a time: _deepWorkGate) ----

    /// <summary>Deep work's daily target (the "goal" document). Raising applies at once; lowering follows the Deep work rule.</summary>
    public Task<GoalChangeResult> SetDeepWorkDailyAsync(int minutes) => GatedAsync(() => SetDailyGatedAsync(minutes));

    /// <summary>
    /// The daily goal from the older screens (the goal card, the start-of-day card, the day plan assistant, the setup assistant; Q375):
    /// one rule for every way in, the stricter one. With the goals ready this is <see cref="SetDeepWorkDailyAsync"/> (raising at once;
    /// lowering waits the looser-change delay and never during a session outside ProductaBuddy mode, and the work-hours rule holds it).
    /// When this PC's goals aren't ready or can't be read, a raise (or no change) is still written at once through the "goal" document,
    /// and a lowering is refused (<see cref="GoalChangeResult.NotAvailable"/>): it can't wait without a place to keep the wait, and
    /// lowering at once would be looser than the rule.
    /// </summary>
    public async Task<GoalChangeResult> SetDailyGoalAsync(int minutes)
    {
        int wanted = Math.Clamp(minutes, MinDailyMinutes, MaxDailyMinutes);
        if (_store.IsReady && _store.Lists.DeepWorkRecord is not null)
        {
            return await SetDeepWorkDailyAsync(wanted);
        }

        int current = _daily.Current.DailyMinutes;
        if (wanted == current)
        {
            return GoalChangeResult.Unchanged;
        }

        if (wanted < current)
        {
            return GoalChangeResult.NotAvailable;
        }

        await _daily.SetAsync(wanted);
        return GoalChangeResult.Applied;
    }

    /// <summary>
    /// What an older daily-goal screen says after <see cref="SetDailyGoalAsync"/> (null when it applied or nothing changed):
    /// "Lowering the goal to 1h waits until 2:14 PM…", "…not during a session", or why it can't be lowered now.
    /// </summary>
    public string? DailyGoalNote(GoalChangeResult result)
    {
        switch (result)
        {
            case GoalChangeResult.Waiting when _store.DeepWork.PendingDaily is GoalPendingChange pending:
            {
                string to = pending.Amount is double amount ? GoalTexts.Minutes(amount) : "no target";
                string at = GoalTexts.Time(pending.AppliesAt, _time.LocalTimeZone);
                return pending.AppliesAt <= Now
                    ? $"Lowering the goal to {to} waits until today's work hours are over (raising it is fine)."
                    : $"Lowering the goal to {to} waits until {at} and never happens during a session (raising it is fine). Cancel it on Goals › Deep work.";
            }

            case GoalChangeResult.Waiting:
                return "Lowering the goal waits like any looser change (raising it is fine).";
            case GoalChangeResult.RefusedDuringSession:
                return "Lowering the goal can't start during a session. Raising it is fine.";
            case GoalChangeResult.NotAvailable:
                return "Lowering the goal isn't possible right now: this PC's goals aren't ready. Raising it is fine.";
            case GoalChangeResult.NotAllowed:
                return "That goal can't be set here.";
            default:
                return null;
        }
    }

    /// <summary>Deep work's weekly target in minutes (null = none). Raising or adding applies at once; lowering or removing follows the Deep work rule.</summary>
    public Task<GoalChangeResult> SetDeepWorkWeeklyAsync(int? minutes) => GatedAsync(() => SetWeeklyGatedAsync(minutes));

    /// <summary>
    /// "Track time only, no target" (on) removes Deep work's daily and weekly targets on the Goals screens: a lowering, so it follows the
    /// Deep work rule. Off brings the daily target back (stricter, at once; the weekly one stays none). The guards keep the "goal"
    /// document's minutes either way (Q374).
    /// </summary>
    public Task<GoalChangeResult> SetTrackTimeOnlyAsync(bool on) => GatedAsync(() => SetTrackTimeOnlyGatedAsync(on));

    /// <summary>Cancel on a waiting Deep work change (the Target tile, Edit goal).</summary>
    public Task CancelDeepWorkPendingAsync(DeepWorkTargetKind kind)
    {
        return GatedAsync(async () =>
        {
            if (_store.IsReady)
            {
                DeepWorkGoalSettings settings = _store.DeepWork;
                DeepWorkGoalSettings cancelled = kind switch
                {
                    DeepWorkTargetKind.Daily => settings with { PendingDaily = null },
                    DeepWorkTargetKind.Weekly => settings with { PendingWeekly = null },
                    _ => settings with { PendingTrackTimeOnly = null },
                };
                if (kind != DeepWorkTargetKind.Daily && settings.PendingStamp is not null && _store.Lists.DeepWorkRecord is Goal record)
                {
                    // ADR 79: a lower value received from another device, cancelled here: this PC's value is stamped again (newer than the
                    // received one), so the other device takes it back as a raise.
                    cancelled = cancelled with { Stamp = NewStamp(settings, record), PendingStamp = null };
                }

                await SaveDeepWorkAsync(cancelled);
            }

            return true;
        });
    }

    /// <summary>
    /// Waiting Deep work changes that may apply now do (<see cref="GoalLowering.CanApply"/>): their time has come, no session runs
    /// (outside ProductaBuddy mode) and the work-hours rule doesn't hold them; one made in ProductaBuddy mode seen in AccountabiliBuddy
    /// mode restarts its full delay first. Runs on every session tick; a tick that finds another Deep work change under way skips.
    /// </summary>
    public async Task ApplyPendingIfReadyAsync()
    {
        if (!_store.IsReady || !_store.DeepWork.HasPending || !await _deepWorkGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            await ApplyPendingGatedAsync();
        }
        finally
        {
            _deepWorkGate.Release();
        }
    }

    /// <summary>
    /// The mode changed (ADR 68). Into AccountabiliBuddy mode, a change still waiting (made in ProductaBuddy mode, where it only waited
    /// for the work-hours rule) waits the full looser-change wait from now, so the switch never makes a lowering land sooner.
    /// </summary>
    public Task ModeChangedAsync()
    {
        return GatedAsync(async () =>
        {
            bool productaBuddy = _isProductaBuddy();
            bool intoGuarding = _wasProductaBuddy && !productaBuddy;
            _wasProductaBuddy = productaBuddy;
            if (!intoGuarding || !_store.IsReady || !_store.DeepWork.HasPending)
            {
                return true;
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
            return true;
        });
    }

    /// <summary>
    /// "goals.deepWork" from a backup's device settings (ADR 71; plan §3b 6), through the same rules as a change made here: a lower weekly
    /// target, no weekly target or Track time only waits (or is refused during a session); a raise applies at once; a waiting change is
    /// never in a backup (and one in an older backup is never taken over). Returns a note for the restore summary, or null.
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

        List<GoalChangeResult> results = await GatedAsync(async () =>
        {
            var steps = new List<GoalChangeResult>();
            if (wanted.TrackTimeOnly)
            {
                steps.Add(await SetTrackTimeOnlyGatedAsync(true));
            }
            else
            {
                if (value.TryGetProperty("trackTimeOnly", out _))
                {
                    steps.Add(await SetTrackTimeOnlyGatedAsync(false));
                }

                steps.Add(await SetWeeklyGatedAsync(wanted.WeeklyMinutes));
            }

            return steps;
        });

        if (results.Contains(GoalChangeResult.RefusedDuringSession))
        {
            return "Deep work's weekly target wasn't restored during a session (it would loosen it).";
        }

        return results.Contains(GoalChangeResult.Waiting)
            ? "Deep work's lower weekly target (or Track time only) from the backup waits the looser-change countdown and never starts during a session."
            : null;
    }

    private async Task<GoalChangeResult> SetDailyGatedAsync(int minutes)
    {
        if (!_store.IsReady || _store.Lists.DeepWorkRecord is not Goal record)
        {
            return GoalChangeResult.NotAvailable;
        }

        int wanted = Math.Clamp(minutes, MinDailyMinutes, MaxDailyMinutes);
        // Setting a daily target ends Track time only: adding a target is stricter, at once.
        if (_store.DeepWork is { } held && (held.TrackTimeOnly || held.PendingTrackTimeOnly is not null))
        {
            await SaveDeepWorkAsync(held with { TrackTimeOnly = false, PendingTrackTimeOnly = null, Stamp = held.TrackTimeOnly ? NewStamp(held, record) : held.Stamp });
        }

        // The guards read the "goal" document's minutes, so lower or not is judged against them.
        var current = new GoalTarget(GoalPeriod.Day, _daily.Current.DailyMinutes, GoalUnit.Minutes);
        GoalChangeDecision decision = GoalLowering.Decide(record, current, wanted, _isProductaBuddy(), _lowerWaitsNow(), Now, _looserWait());
        switch (decision.Outcome)
        {
            case GoalChangeOutcome.Unchanged:
                await SaveDeepWorkAsync(_store.DeepWork with { PendingDaily = null });
                return GoalChangeResult.Unchanged;
            case GoalChangeOutcome.ApplyNow:
                await WriteDailyAsync(wanted);
                await SaveDeepWorkAsync(_store.DeepWork with { PendingDaily = null });
                return GoalChangeResult.Applied;
            default:
                return await KeepWaitingAsync(
                    settings => settings.PendingDaily, decision.Pending(wanted)!, (settings, pending) => settings with { PendingDaily = pending });
        }
    }

    private async Task<GoalChangeResult> SetWeeklyGatedAsync(int? minutes)
    {
        if (!_store.IsReady || _store.Lists.DeepWorkRecord is not Goal record)
        {
            return GoalChangeResult.NotAvailable;
        }

        int? wanted = minutes is int value ? Math.Clamp(value, MinWeeklyMinutes, MaxWeeklyMinutes) : null;
        if (wanted is not null && _store.DeepWork is { } held && (held.TrackTimeOnly || held.PendingTrackTimeOnly is not null))
        {
            // A weekly target ends Track time only: stricter, at once.
            await SaveDeepWorkAsync(held with
            {
                TrackTimeOnly = false,
                PendingTrackTimeOnly = null,
                Stamp = held.TrackTimeOnly ? NewStamp(held, record) : held.Stamp,
            });
        }

        DeepWorkGoalSettings settings = _store.DeepWork;
        var current = new GoalTarget(GoalPeriod.Week, settings.TrackTimeOnly ? null : settings.WeeklyMinutes, GoalUnit.Minutes);
        GoalChangeDecision decision = GoalLowering.Decide(record, current, wanted, _isProductaBuddy(), _lowerWaitsNow(), Now, _looserWait());
        switch (decision.Outcome)
        {
            case GoalChangeOutcome.Unchanged:
                await SaveDeepWorkAsync(_store.DeepWork with { PendingWeekly = null });
                return GoalChangeResult.Unchanged;
            case GoalChangeOutcome.ApplyNow:
                await SaveDeepWorkAsync(_store.DeepWork with { WeeklyMinutes = wanted, PendingWeekly = null, Stamp = NewStamp(_store.DeepWork, record) });
                return GoalChangeResult.Applied;
            default:
                return await KeepWaitingAsync(
                    held => held.PendingWeekly, decision.Pending(wanted)!,
                    (held, pending) => held with { PendingWeekly = pending, PendingStamp = null, PendingAfterSession = false });
        }
    }

    private async Task<GoalChangeResult> SetTrackTimeOnlyGatedAsync(bool on)
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

            await SaveDeepWorkAsync(settings with
            {
                TrackTimeOnly = false,
                PendingTrackTimeOnly = null,
                Stamp = settings.TrackTimeOnly ? NewStamp(settings, record) : settings.Stamp,
            });
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
            await SaveDeepWorkAsync(TrackingTimeOnly(_store.DeepWork) with { Stamp = NewStamp(_store.DeepWork, record) });
            return GoalChangeResult.Applied;
        }

        return await KeepWaitingAsync(
            held => held.PendingTrackTimeOnly, decision.Pending(null)!,
            (held, pending) => held with { PendingTrackTimeOnly = pending, PendingStamp = null, PendingAfterSession = false });
    }

    private async Task ApplyPendingGatedAsync()
    {
        if (!_store.IsReady || !_store.DeepWork.HasPending || _store.Lists.DeepWorkRecord is null)
        {
            return;
        }

        DateTimeOffset now = Now;
        bool productaBuddy = _isProductaBuddy();
        bool session = SessionRunning;
        bool lowerWaits = _lowerWaitsNow();
        bool Ready(GoalPendingChange? pending) => pending is not null && GoalLowering.CanApply(pending, now, session, productaBuddy, lowerWaits);

        DeepWorkGoalSettings settings = _store.DeepWork;
        if (settings.PendingAfterSession && (productaBuddy || !session))
        {
            // ADR 79: a looser value received during a session waits its delay from the session's end (none in ProductaBuddy mode).
            DateTimeOffset from = now + _looserWait();
            GoalPendingChange? Rebased(GoalPendingChange? pending) =>
                pending is null || productaBuddy || pending.AppliesAt >= from ? pending : pending with { AppliesAt = from };
            settings = settings with
            {
                PendingWeekly = Rebased(settings.PendingWeekly),
                PendingTrackTimeOnly = Rebased(settings.PendingTrackTimeOnly),
                PendingAfterSession = false,
            };
        }

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

        // A received change lands with its sender's stamp (nothing echoes); one made here is stamped as it lands (ADR 79).
        if (Ready(settings.PendingTrackTimeOnly))
        {
            GoalFieldStamp stamp = settings.PendingStamp ?? NewStamp(settings, _store.Lists.DeepWorkRecord!);
            settings = TrackingTimeOnly(settings) with { Stamp = stamp, PendingStamp = null };
        }

        if (Ready(settings.PendingWeekly))
        {
            GoalFieldStamp stamp = settings.PendingStamp ?? NewStamp(settings, _store.Lists.DeepWorkRecord!);
            settings = settings with
            {
                WeeklyMinutes = settings.PendingWeekly!.Amount is double weekly ? (int?)(int)weekly : null,
                PendingWeekly = null,
                Stamp = stamp,
                PendingStamp = null,
            };
        }

        int? dailyTo = null;
        int dailySeen = _daily.Current.DailyMinutes;
        if (Ready(settings.PendingDaily))
        {
            // Still a lowering of what the guards read (a later raise elsewhere drops it before this; checked again here).
            if (settings.PendingDaily!.Amount is double daily && daily < dailySeen)
            {
                dailyTo = (int)daily;
            }

            settings = settings with { PendingDaily = null };
        }

        // Deep work's own values first (nothing else changes them while the gate is held), then the "goal" document, only if nobody
        // set it meanwhile: the goal card, another PC or a restore can raise it during the save above (outside this gate), and that
        // later choice wins; the waiting lowering is then dropped, never written over it (R4 milestone review).
        await SaveDeepWorkAsync(settings);
        if (dailyTo is int lowered && _daily.Current.DailyMinutes == dailySeen)
        {
            await WriteDailyAsync(lowered);
        }
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
            || !IsLiveArea(goal.AreaId) || !goal.Sources.All(rule => GoalSources.AllowedFor(goal with { BuiltIn = false }, rule.Type)))
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
            if (goal.BuiltIn || !goal.Sources.All(rule => GoalSources.AllowedFor(goal with { BuiltIn = false }, rule.Type)))
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

    /// <summary>
    /// Delete a goal (at once): a tombstone with no text (<see cref="GoalTombstones.Delete"/>, ADR 79). Its entries and reminders stay, hidden
    /// while the goal isn't live, and come back with it if a later edit on another device revives it; they go with its tombstone (30 days).
    /// Deep work can't be deleted.
    /// </summary>
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
            Goals = Replace(lists.Goals, item => item.Id == goalId, GoalTombstones.Delete(goal, SharedListMerge.NextStamp(now, goal))),
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

        // ADR 48: a local edit is stamped after the version it replaces (the held one, a tombstone included), read inside the change.
        await _store.ChangeAsync(source, (lists, now) => lists with
        {
            Areas = Upsert(lists.Areas, item => item.Id == area.Id,
                area with { DeletedAt = null, EditedAt = SharedListMerge.NextStamp(now, lists.Areas.FirstOrDefault(item => item.Id == area.Id)) }),
        });
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
            Areas = Replace(lists.Areas, item => item.Id == areaId, Tomb(area, lists.Areas, areaId, now)),
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

        // "Your latest decision wins" (GoalCounting.LatestHand orders by edit time, then id): a hand entry saved now is stamped after every
        // other hand entry of that goal and day, so a tick and an untick within one clock reading never tie on a random id; and (ADR 48)
        // after the version of its own id it replaces. Read inside the change, so a version that arrived meanwhile counts.
        await _store.ChangeAsync(source, (lists, now) =>
        {
            DateTimeOffset stamp = SharedListMerge.NextStamp(now, lists.Entries.FirstOrDefault(item => item.Id == entry.Id));
            if (entry.Source == GoalEntrySource.Hand)
            {
                DateTimeOffset latest = lists.Entries
                    .Where(item => item.GoalId == entry.GoalId && item.Day == entry.Day && item.Source == GoalEntrySource.Hand && item.Id != entry.Id)
                    .Select(item => item.Stamp)
                    .DefaultIfEmpty(DateTimeOffset.MinValue)
                    .Max();
                if (latest >= stamp)
                {
                    stamp = latest.AddTicks(1);
                }
            }

            GoalEntry saved = entry with { DeletedAt = null, EditedAt = stamp };
            return lists with { Entries = Upsert(lists.Entries, item => item.Id == entry.Id, saved) };
        });
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

        await _store.ChangeAsync(source, (lists, now) => lists with { Entries = Replace(lists.Entries, item => item.Id == entryId, Tomb(entry, lists.Entries, entryId, now)) });
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

        await _store.ChangeAsync(source, (lists, now) => lists with
        {
            Reminders = Upsert(lists.Reminders, item => item.Id == reminder.Id,
                reminder with { DeletedAt = null, EditedAt = SharedListMerge.NextStamp(now, lists.Reminders.FirstOrDefault(item => item.Id == reminder.Id)) }),
        });
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
            Reminders = Replace(lists.Reminders, item => item.Id == reminderId, Tomb(reminder with { Reason = null }, lists.Reminders, reminderId, now)),
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

        await _store.ChangeAsync(source, (lists, now) => lists with
        {
            Reviews = Upsert(lists.Reviews, item => item.Id == review.Id,
                review with { DeletedAt = null, EditedAt = SharedListMerge.NextStamp(now, lists.Reviews.FirstOrDefault(item => item.Id == review.Id)) }),
        });
        return GoalChangeResult.Applied;
    }

    /// <summary>The page's UI state (display only: at once).</summary>
    public Task SetUiPreferencesAsync(GoalUiPreferences preferences, object? source = null) => _store.SetUiAsync(preferences, source);

    /// <summary>Options › Charts (week start, heatmap labels, extra tiles; at once in both modes).</summary>
    public Task SetPageSettingsAsync(GoalPageSettings settings, object? source = null) => _store.SetPageSettingsAsync(settings, source);

    // ---- ADR 79: another device's goals ----

    /// <summary>
    /// Another device's goals merged with this PC's (<see cref="Sync.GoalListSync"/>) taken in: the lists as merged, and Deep work's record
    /// split (<see cref="GoalDeepWorkSync.Split"/>) so the weekly target and "Track time only" it carried go through this PC's rule (the
    /// class summary) and the stored record keeps this PC's own stamp. One store change for both (<see cref="GoalStore.ApplySyncedAsync"/>).
    /// Refused (false) unless the goals are ready. <paramref name="merge"/> takes this PC's held goals and the received ones together and
    /// is called INSIDE the gate, right before the result is stored with nothing awaited in between, so a change saved while this call
    /// waited for the gate is in what is merged and is never overwritten by a stale result; null from it = nothing to take in.
    /// </summary>
    /// <returns>Whether anything changed here.</returns>
    public Task<bool> ApplySyncedAsync(Func<GoalSyncLists?> merge, object? source = null) => GatedAsync(() => ApplySyncedGatedAsync(merge, source));

    private async Task<bool> ApplySyncedGatedAsync(Func<GoalSyncLists?> merge, object? source)
    {
        if (!_store.IsReady || _store.Lists.DeepWorkRecord is not Goal ownRecord || merge() is not GoalSyncLists merged)
        {
            return false;
        }

        IReadOnlyList<Goal> known = merged.Goals is GoalNewerList<Goal> carrier ? carrier.Known : merged.Goals;
        int index = known.ToList().FindIndex(goal => goal.Id == Goal.DeepWorkId && goal.IsLive);
        if (index < 0)
        {
            return await _store.ApplySyncedAsync(merged, null, source);
        }

        GoalFieldStamp own = OwnStamp(_store.DeepWork, ownRecord);
        (Goal stored, GoalDeepWorkTargets received) = GoalDeepWorkSync.Split(known[index]);
        DeepWorkGoalSettings settings = Received(_store.DeepWork, received, own, ownRecord);
        // The stored record keeps its own weekly-part stamp: the one in effect is Deep work's settings' (they always hold one from here
        // on), so a merge never rewrites the record for it (no change, no event, no echo).
        GoalFieldStamp? storedStamp = GoalFieldStamps.For(ownRecord).Of(GoalField.SecondTarget);
        var goals = known.ToList();
        goals[index] = stored with { Stamps = GoalFieldStamps.For(stored).With(GoalField.SecondTarget, storedStamp) };
        GoalSyncLists lists = merged with { Goals = GoalNewerJson.WithNewer(goals, GoalNewerJson.NewerOf(merged.Goals)) };
        return await _store.ApplySyncedAsync(lists, settings, source);
    }

    // The weekly target and "Track time only" another device sent, against the ones in effect here (stamped "own").
    private DeepWorkGoalSettings Received(DeepWorkGoalSettings held, GoalDeepWorkTargets received, GoalFieldStamp own, Goal record)
    {
        DeepWorkGoalSettings settings = held.Stamp is null ? held with { Stamp = own } : held;
        if (received.Stamp == own || !IsNewer(received.Stamp, own))
        {
            // The same, or older than what is in effect here (a stale file, say 600 after a raise to 900): nothing to take.
            return settings;
        }

        int? wanted = received.TrackTimeOnly || received.WeeklyMinutes is not double minutes ? null : Math.Max(1, (int)Math.Round(minutes));
        bool sameInEffect = received.TrackTimeOnly == settings.TrackTimeOnly && (settings.TrackTimeOnly || wanted == settings.WeeklyMinutes);
        if (sameInEffect)
        {
            // The same values: their newer stamp is taken; a change received earlier and still waiting is older than it and goes.
            return settings.PendingStamp is null
                ? settings with { Stamp = received.Stamp }
                : settings with { Stamp = received.Stamp, PendingWeekly = null, PendingTrackTimeOnly = null, PendingStamp = null, PendingAfterSession = false };
        }

        bool looser = received.TrackTimeOnly || (!settings.TrackTimeOnly && GoalLowering.IsLooser(GoalDirection.AtLeast, settings.WeeklyMinutes, wanted));
        if (!looser)
        {
            // Higher, new, or "Track time only" turning off: stricter, at once (also during a session); any waiting weekly change goes.
            return settings with
            {
                WeeklyMinutes = wanted,
                TrackTimeOnly = false,
                Stamp = received.Stamp,
                PendingWeekly = null,
                PendingTrackTimeOnly = null,
                PendingStamp = null,
                PendingAfterSession = false,
            };
        }

        DateTimeOffset now = Now;
        bool productaBuddy = _isProductaBuddy();
        GoalPendingChange? running = received.TrackTimeOnly ? settings.PendingTrackTimeOnly : settings.PendingWeekly;
        if (running is not null && settings.PendingStamp == received.Stamp && Nullable.Equals(running.Amount, (double?)wanted))
        {
            // Already waiting for this very change: its time stays (the same file read again).
            return settings;
        }

        GoalPendingChange pending;
        bool afterSession = false;
        if (!productaBuddy && SessionRunning)
        {
            // Held while the session runs; its wait counts from the session's end (ApplyPendingIfReadyAsync).
            pending = new GoalPendingChange(wanted, now + _looserWait());
            afterSession = true;
        }
        else
        {
            GoalTarget current = received.TrackTimeOnly
                ? new GoalTarget(GoalPeriod.Day, _daily.Current.DailyMinutes, GoalUnit.Minutes)
                : new GoalTarget(GoalPeriod.Week, settings.WeeklyMinutes, GoalUnit.Minutes);
            GoalChangeDecision decision = GoalLowering.Decide(record, current, wanted, productaBuddy, _lowerWaitsNow(), now, _looserWait());
            if (decision.Outcome != GoalChangeOutcome.Wait)
            {
                DeepWorkGoalSettings applied = received.TrackTimeOnly ? TrackingTimeOnly(settings) : settings with { WeeklyMinutes = wanted, PendingWeekly = null, PendingTrackTimeOnly = null };
                return applied with { Stamp = received.Stamp, PendingStamp = null, PendingAfterSession = false };
            }

            pending = decision.Pending(wanted)!;
        }

        return received.TrackTimeOnly
            ? settings with { PendingTrackTimeOnly = pending, PendingWeekly = null, PendingStamp = received.Stamp, PendingAfterSession = afterSession }
            : settings with { PendingWeekly = pending, PendingTrackTimeOnly = null, PendingStamp = received.Stamp, PendingAfterSession = afterSession };
    }

    // The merge's order for two stamps: the later time, then the device id.
    private static bool IsNewer(GoalFieldStamp candidate, GoalFieldStamp than)
    {
        int byTime = candidate.At.UtcTicks.CompareTo(than.At.UtcTicks);
        return byTime != 0 ? byTime > 0 : string.CompareOrdinal(candidate.By.ToString("D"), than.By.ToString("D")) > 0;
    }

    // The stamp of the weekly target in effect here: the stored one, else the record's own part stamp (a store from before GS3a).
    private static GoalFieldStamp OwnStamp(DeepWorkGoalSettings settings, Goal record)
    {
        return settings.Stamp ?? GoalFieldStamps.For(record).Of(GoalField.SecondTarget)!;
    }

    // A change made here: now, by this device, after every stamp it replaces (a faster clock's received stamp included).
    private GoalFieldStamp NewStamp(DeepWorkGoalSettings settings, Goal record)
    {
        DateTimeOffset at = Now;
        foreach (GoalFieldStamp? earlier in new[] { OwnStamp(settings, record), settings.PendingStamp })
        {
            if (earlier is not null && earlier.At >= at)
            {
                at = earlier.At.AddTicks(1);
            }
        }

        return new GoalFieldStamp(at, Self());
    }

    // Deep work's values saved: a received change's stamp and hold go once no weekly or "Track time only" change waits.
    private Task SaveDeepWorkAsync(DeepWorkGoalSettings settings)
    {
        DeepWorkGoalSettings tidied = settings.PendingWeekly is null && settings.PendingTrackTimeOnly is null
            ? settings with { PendingStamp = null, PendingAfterSession = false }
            : settings;
        return _store.SetDeepWorkAsync(tidied);
    }

    // ---- helpers ----

    // Keeps a looser change waiting (refused during a session outside ProductaBuddy mode); built on Deep work's values as they are now.
    private async Task<GoalChangeResult> KeepWaitingAsync(
        Func<DeepWorkGoalSettings, GoalPendingChange?> held, GoalPendingChange wanted, Func<DeepWorkGoalSettings, GoalPendingChange, DeepWorkGoalSettings> with)
    {
        if (!_isProductaBuddy() && SessionRunning)
        {
            return GoalChangeResult.RefusedDuringSession;
        }

        // A wait already running for the same value keeps its time.
        DeepWorkGoalSettings settings = _store.DeepWork;
        if (held(settings) is { } running && Nullable.Equals(running.Amount, wanted.Amount))
        {
            return GoalChangeResult.Waiting;
        }

        await SaveDeepWorkAsync(with(settings, wanted));
        return GoalChangeResult.Waiting;
    }

    private async Task<T> GatedAsync<T>(Func<Task<T>> change)
    {
        await _deepWorkGate.WaitAsync();
        try
        {
            return await change();
        }
        finally
        {
            _deepWorkGate.Release();
        }
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

    // The daily goal was set somewhere else (the goal card, the start-of-day card, another PC, a restore): that later choice wins, so the
    // waiting lower value seen at that moment is dropped rather than landing over it (one asked for after it stays). This service's own
    // write is told apart at once, before waiting for the gate.
    private void DailyChangedElsewhere()
    {
        if (_writingDaily || !_store.IsReady || _store.DeepWork.PendingDaily is not GoalPendingChange seen)
        {
            return;
        }

        _ = GatedAsync(async () =>
        {
            if (_store.DeepWork.PendingDaily == seen)
            {
                await _store.SetDeepWorkAsync(_store.DeepWork with { PendingDaily = null });
            }

            return true;
        });
    }

    private static bool SameGoal(Goal left, Goal right)
    {
        return JsonSerializer.Serialize(left with { EditedAt = default }, Core.Sync.SyncJson.Options)
            == JsonSerializer.Serialize(right with { EditedAt = default }, Core.Sync.SyncJson.Options);
    }

    private bool IsLiveArea(Guid areaId) => _store.Lists.Areas.Any(area => area.IsLive && area.Id == areaId);

    // A tombstone of an item, stamped after the held version of that id (ADR 48), so it wins the merge on every device.
    private static T Tomb<T>(T item, IReadOnlyList<T> held, Guid id, DateTimeOffset now) where T : ISharedItem
    {
        DateTimeOffset at = SharedListMerge.NextStamp(now, held.FirstOrDefault(other => other.Id == id) ?? item);
        return (T)item.WithStamps(at, at);
    }

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
