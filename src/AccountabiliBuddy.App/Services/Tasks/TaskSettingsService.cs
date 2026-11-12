using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Tasks;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// Loads and saves <see cref="TaskSettings"/> (document "tasks.settings", this PC only), kept per mode (ADR 69): AccountabiliBuddy and
/// ProductaBuddy each have their own copy with the same defaults, and the active mode's is <see cref="Current"/>. Stricter changes apply
/// at once; looser ones (more actions per session, pausing the timer when stepping away or on a side task) are refused while a session runs and otherwise
/// wait the looser-change wait (<see cref="LockSettings.LooseningDelay"/>, counted by the trusted clock and kept in the document so a
/// restart doesn't skip it), taking effect only while no session runs; the same rules hold for either mode's copy (a backup restores
/// both through here). A running session never gets looser from a change or a mode switch: it keeps the number of actions it started
/// with (<see cref="SessionChecklist.ActionCap"/>), and a step away or a side task takes the stricter of its setting at the session's
/// start and now (<see cref="StepAwayTimerPolicy.Stricter"/>). Use from the UI thread only.
/// </summary>
public sealed class TaskSettingsService
{
    public const string DocumentKey = "tasks.settings";

    private readonly SqliteJsonDocumentStore _store;
    private readonly TimeProvider _time;
    private readonly SessionController _sessions;
    private readonly Func<TimeSpan> _looserWait;
    private ModeSettings? _modes;

    /// <param name="looserWait">How long a looser change waits: the looser-change wait of the configuration in effect.</param>
    public TaskSettingsService(SqliteJsonDocumentStore store, TimeProvider time, SessionController sessions, Func<TimeSpan> looserWait)
    {
        _store = store;
        _time = time;
        _sessions = sessions;
        _looserWait = looserWait;
        _sessions.Ticked += (_, _) => ApplyPendingIfReady();
    }

    /// <summary>The active mode's settings.</summary>
    public TaskSettings Current { get; private set; } = TaskSettings.Default;

    /// <summary>How many actions the next session holds (2 to 5).</summary>
    public int SessionActions => Core.Tasks.SessionActions.ClampCap(Current.SessionActions);

    public bool NextStepTakesSlot => Current.NextStepTakesSlot;

    /// <summary>
    /// "When I step away" for a step away now (<see cref="StepAwayTimerPolicy.ForStepAway"/>): the stricter of the setting the running
    /// session started with (kept in its state and checkpoint, so a restart keeps it) and the active mode's now, so neither a change, a
    /// mode switch nor a restart loosens it.
    /// </summary>
    public StepAwayTimer StepAwayNow => StepAwayTimerPolicy.ForStepAway(_sessions.State, Current.StepAway);

    /// <summary>
    /// "When I start a side task" for a side task now (<see cref="StepAwayTimerPolicy.ForSideTask"/>): the stricter of the setting the
    /// running session started with (kept in its state and checkpoint) and the active mode's now, as <see cref="StepAwayNow"/>.
    /// </summary>
    public StepAwayTimer SideTaskNow => StepAwayTimerPolicy.ForSideTask(_sessions.State, Current.SideTask);

    private GuidanceMode ActiveMode => _modes?.Mode ?? GuidanceMode.Guarding;

    public event EventHandler? Changed;

    /// <summary>The settings are kept per mode from now on (ADR 69): the active mode's copy is read now and after every mode switch.</summary>
    public void UseModes(ModeSettings modes)
    {
        _modes = modes;
        modes.Reload += (_, _) =>
        {
            LoadBlocking();
            Changed?.Invoke(this, EventArgs.Empty);
        };
        LoadBlocking();
    }

    public void LoadBlocking()
    {
        try
        {
            string key = _modes?.ReadKeyBlocking(DocumentKey, TaskSettings.Default) ?? DocumentKey;
            TaskSettings? loaded = Task.Run(() => _store.LoadAsync<TaskSettings>(key)).GetAwaiter().GetResult();
            Current = loaded is null ? TaskSettings.Default : Clamped(loaded);
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Current = TaskSettings.Default;
        }
    }

    /// <summary>
    /// The number of actions a session holds, in <paramref name="mode"/>'s copy (the active mode's by default). Fewer applies at once
    /// (and drops a waiting larger number); more waits the looser-change wait and is refused while a session runs.
    /// </summary>
    /// <returns>False when it was refused (a larger number while a session runs).</returns>
    public async Task<bool> SetSessionActionsAsync(int actions, GuidanceMode? mode = null)
    {
        GuidanceMode target = mode ?? ActiveMode;
        TaskSettings settings = await LoadForAsync(target);
        int wanted = Core.Tasks.SessionActions.ClampCap(actions);
        if (wanted <= Core.Tasks.SessionActions.ClampCap(settings.SessionActions))
        {
            await SaveForAsync(target, settings with { SessionActions = wanted, PendingSessionActions = null, PendingFrom = null });
            return true;
        }

        if (_sessions.State.IsActive)
        {
            return false;
        }

        // A wait already running for the same number keeps its time.
        if (settings.PendingSessionActions == wanted && settings.PendingFrom is not null)
        {
            return true;
        }

        await SaveForAsync(target, settings with { PendingSessionActions = wanted, PendingFrom = _time.GetUtcNow() + _looserWait() });
        return true;
    }

    /// <summary>
    /// "When I step away" (ADR 77), in <paramref name="mode"/>'s copy. Keeping the timer running applies at once (and drops a waiting
    /// pause); pausing it is looser: it waits the looser-change wait and is refused while a session runs.
    /// </summary>
    /// <returns>False when it was refused (pausing while a session runs).</returns>
    public async Task<bool> SetStepAwayAsync(StepAwayTimer wanted, GuidanceMode? mode = null)
    {
        GuidanceMode target = mode ?? ActiveMode;
        TaskSettings settings = await LoadForAsync(target);
        if (!StepAwayTimerPolicy.IsLooser(settings.StepAway, wanted))
        {
            await SaveForAsync(target, settings with { StepAway = wanted, PendingStepAway = null, PendingStepAwayFrom = null });
            return true;
        }

        if (_sessions.State.IsActive)
        {
            return false;
        }

        if (settings.PendingStepAway == wanted && settings.PendingStepAwayFrom is not null)
        {
            return true;
        }

        await SaveForAsync(target, settings with { PendingStepAway = wanted, PendingStepAwayFrom = _time.GetUtcNow() + _looserWait() });
        return true;
    }

    /// <summary>
    /// "When I start a side task" (ADR 77), in <paramref name="mode"/>'s copy, with the rules of <see cref="SetStepAwayAsync"/>: keeping
    /// the timer running applies at once (and drops a waiting pause); pausing it is looser (the session ends later): it waits the
    /// looser-change wait and is refused while a session runs.
    /// </summary>
    /// <returns>False when it was refused (pausing while a session runs).</returns>
    public async Task<bool> SetSideTaskAsync(StepAwayTimer wanted, GuidanceMode? mode = null)
    {
        GuidanceMode target = mode ?? ActiveMode;
        TaskSettings settings = await LoadForAsync(target);
        if (!StepAwayTimerPolicy.IsLooser(settings.SideTask, wanted))
        {
            await SaveForAsync(target, settings with { SideTask = wanted, PendingSideTask = null, PendingSideTaskFrom = null });
            return true;
        }

        if (_sessions.State.IsActive)
        {
            return false;
        }

        if (settings.PendingSideTask == wanted && settings.PendingSideTaskFrom is not null)
        {
            return true;
        }

        await SaveForAsync(target, settings with { PendingSideTask = wanted, PendingSideTaskFrom = _time.GetUtcNow() + _looserWait() });
        return true;
    }

    /// <summary>"The next step takes the slot" (on by default). Either way it loosens nothing: it only says which step is in focus.</summary>
    public async Task SetNextStepTakesSlotAsync(bool on, GuidanceMode? mode = null)
    {
        GuidanceMode target = mode ?? ActiveMode;
        TaskSettings settings = await LoadForAsync(target);
        if (on != settings.NextStepTakesSlot)
        {
            await SaveForAsync(target, settings with { NextStepTakesSlot = on });
        }
    }

    /// <summary>Untouched after this many weeks (1 to 4). It only decides what the tidy-up and the "Untouched for" filter call old, so it loosens nothing and applies at once.</summary>
    public Task SetUntouchedWeeksAsync(int weeks, GuidanceMode? mode = null)
    {
        return UpdateAsync(settings => settings with { UntouchedWeeks = TaskUntouched.ClampWeeks(weeks) }, mode);
    }

    /// <summary>The weekly tidy-up card: on or off, and the day it shows from.</summary>
    public Task SetTidyUpAsync(bool on, DayOfWeek day, GuidanceMode? mode = null)
    {
        return UpdateAsync(settings => settings with { TidyUpOn = on, TidyUpDay = day }, mode);
    }

    /// <summary>Whether new views pin highlighted tasks to the top.</summary>
    public Task SetPinHighlightsAsync(bool on, GuidanceMode? mode = null)
    {
        return UpdateAsync(settings => settings with { PinHighlights = on }, mode);
    }

    /// <summary>
    /// The default view this PC kept before the setting became the account's: AccountabiliBuddy's copy first, then ProductaBuddy's
    /// (only a copy of its own); null when neither has one.
    /// </summary>
    public Guid? LegacyDefaultViewBlocking()
    {
        try
        {
            return Task.Run(async () =>
            {
                foreach (GuidanceMode mode in new[] { GuidanceMode.Guarding, GuidanceMode.Companion })
                {
                    if (await _store.LoadAsync<TaskSettings>(ModeSettings.KeyFor(DocumentKey, mode)) is { DefaultViewId: Guid id } && id != Guid.Empty)
                    {
                        return (Guid?)id;
                    }
                }

                return null;
            }).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Clears the old device-local default view in each mode's copy that holds the one the account took over (or an empty one), once
    /// the account has it. A different value stays where it is, unused, so nothing is destroyed (the account holds one default view).
    /// </summary>
    public async Task ClearLegacyDefaultViewAsync(Guid adopted)
    {
        foreach (GuidanceMode mode in new[] { GuidanceMode.Guarding, GuidanceMode.Companion })
        {
            string key = ModeSettings.KeyFor(DocumentKey, mode);
            if (await _store.LoadAsync<TaskSettings>(key) is { DefaultViewId: Guid held } own && (held == adopted || held == Guid.Empty))
            {
                TaskSettings cleared = own with { DefaultViewId = null };
                await _store.SaveAsync(key, cleared);
                if (mode == ActiveMode)
                {
                    Current = Clamped(cleared);
                }
            }
        }
    }

    /// <summary>The project markers the user taught (Settings › Tasks › Project markers).</summary>
    public Task SetCustomMarkersAsync(IReadOnlyList<ProjectMarkerPattern> markers, GuidanceMode? mode = null)
    {
        return UpdateAsync(settings => settings with { CustomMarkers = markers.Count == 0 ? null : markers.Distinct().ToList() }, mode);
    }

    /// <summary>The add box's "Treat … as a project marker?" answered Yes: the style joins the taught markers.</summary>
    public Task LearnMarkerAsync(ProjectMarkerPattern marker)
    {
        return UpdateAsync(settings => settings with { CustomMarkers = [.. (settings.CustomMarkers ?? []).Where(held => held != marker), marker] }, null);
    }

    /// <summary>The add box's "Treat … as a project marker?" answered No: that style is never offered again.</summary>
    public Task DeclineMarkerAsync(ProjectMarkerPattern marker)
    {
        return UpdateAsync(settings => settings with { DeclinedMarkers = [.. (settings.DeclinedMarkers ?? []).Where(held => held != marker), marker] }, null);
    }

    private async Task UpdateAsync(Func<TaskSettings, TaskSettings> change, GuidanceMode? mode)
    {
        GuidanceMode target = mode ?? ActiveMode;
        TaskSettings settings = await LoadForAsync(target);
        TaskSettings changed = Clamped(change(settings));
        if (changed != settings)
        {
            await SaveForAsync(target, changed);
        }
    }

    /// <summary>
    /// A backup's copy for <paramref name="mode"/> (ADR 71), through the same rules as a change made here: a waiting change in the
    /// backup is never taken over, and a looser value waits or is refused. An older backup's default view is not this PC's any more:
    /// the caller hands it to <see cref="TaskAccountSettingsService.AdoptLegacyAsync"/>.
    /// </summary>
    /// <returns>A note for the restore summary, or null.</returns>
    public async Task<string?> RestoreAsync(TaskSettings wanted, GuidanceMode mode)
    {
        TaskSettings before = await LoadForAsync(mode);
        await SetNextStepTakesSlotAsync(wanted.NextStepTakesSlot, mode);
        await UpdateAsync(
            settings => settings with
            {
                UntouchedWeeks = wanted.UntouchedWeeks,
                TidyUpOn = wanted.TidyUpOn,
                TidyUpDay = wanted.TidyUpDay,
                PinHighlights = wanted.PinHighlights,
                CustomMarkers = wanted.CustomMarkers,
                DeclinedMarkers = wanted.DeclinedMarkers,
            },
            mode);
        bool actionsTaken = await SetSessionActionsAsync(wanted.SessionActions, mode);
        bool stepAwayTaken = await SetStepAwayAsync(wanted.StepAway, mode);
        bool sideTaskTaken = await SetSideTaskAsync(wanted.SideTask, mode);
        if (!actionsTaken || !stepAwayTaken || !sideTaskTaken)
        {
            return "Some Tasks settings weren't restored during a session (they would loosen it).";
        }

        TaskSettings after = await LoadForAsync(mode);
        bool waiting = (after.PendingSessionActions is not null && after.PendingSessionActions != before.PendingSessionActions)
            || (after.PendingStepAway is not null && after.PendingStepAway != before.PendingStepAway)
            || (after.PendingSideTask is not null && after.PendingSideTask != before.PendingSideTask);
        return waiting
            ? "Looser Tasks settings (more actions per session, pausing the timer when stepping away or on a side task) take effect after the countdown."
            : null;
    }

    // Looser changes that waited long enough take effect, in the active mode's copy, only while no session runs.
    private void ApplyPendingIfReady()
    {
        TaskSettings settings = Current;
        DateTimeOffset now = _time.GetUtcNow();
        if (_sessions.State.IsActive)
        {
            return;
        }

        TaskSettings applied = settings;
        if (settings.PendingSessionActions is int pending && settings.PendingFrom is DateTimeOffset from && now >= from)
        {
            applied = applied with { SessionActions = pending, PendingSessionActions = null, PendingFrom = null };
        }

        if (settings.PendingStepAway is StepAwayTimer waiting && settings.PendingStepAwayFrom is DateTimeOffset stepFrom && now >= stepFrom)
        {
            applied = applied with { StepAway = waiting, PendingStepAway = null, PendingStepAwayFrom = null };
        }

        if (settings.PendingSideTask is StepAwayTimer sideWaiting && settings.PendingSideTaskFrom is DateTimeOffset sideFrom && now >= sideFrom)
        {
            applied = applied with { SideTask = sideWaiting, PendingSideTask = null, PendingSideTaskFrom = null };
        }

        if (applied != settings)
        {
            _ = SaveForAsync(ActiveMode, applied);
        }
    }

    private async Task<TaskSettings> LoadForAsync(GuidanceMode mode)
    {
        if (mode == ActiveMode)
        {
            return Current;
        }

        // The other mode's copy; ProductaBuddy's not made yet starts as AccountabiliBuddy's (ModeSettings.ReadKeyBlocking does the same).
        TaskSettings? own = await _store.LoadAsync<TaskSettings>(ModeSettings.KeyFor(DocumentKey, mode));
        TaskSettings? start = own ?? (mode == GuidanceMode.Companion ? await _store.LoadAsync<TaskSettings>(DocumentKey) : null);
        return start is null ? TaskSettings.Default : Clamped(start);
    }

    private async Task SaveForAsync(GuidanceMode mode, TaskSettings settings)
    {
        TaskSettings clamped = Clamped(settings);
        await _store.SaveAsync(ModeSettings.KeyFor(DocumentKey, mode), clamped);
        if (mode == ActiveMode)
        {
            Current = clamped;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static TaskSettings Clamped(TaskSettings settings)
    {
        return settings with
        {
            SessionActions = Core.Tasks.SessionActions.ClampCap(settings.SessionActions),
            PendingSessionActions = settings.PendingSessionActions is int pending ? Core.Tasks.SessionActions.ClampCap(pending) : null,
            UntouchedWeeks = TaskUntouched.ClampWeeks(settings.UntouchedWeeks),
        };
    }
}
