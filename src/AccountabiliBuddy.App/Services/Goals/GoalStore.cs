using System.Security.Cryptography;
using System.Text.Json;
using AccountabiliBuddy.Core.Goals;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>
/// This PC's goals (round 2 R4.3; brief §5.1, §5.2): areas, goals, entries, reminders and weekly reviews, each list a DPAPI-sealed
/// document ("goals.items", "goals.areas", "goals.entries", "goals.reminders", "goals.reviews") written together in one transaction,
/// plus plain device-local documents: Deep work's weekly target and Track time only ("goals.deepWork", a device setting), Deep work's
/// waiting changes ("goals.deepWork.pending", state: never in a backup), the page's UI state ("goals.ui") and its Options › Charts
/// ("goals.settings"). Nothing here syncs until ADR 79 is accepted; the items keep their ids, positions, edit stamps and tombstones so
/// turning sync on later is a merge. Names (goals, areas, a source's #tag or app), notes and reasons are private text: sealed at rest with
/// this PC's key and never logged; in a backup the names travel readable like task titles, notes and reasons only with a password. The lists go in backups
/// with the task lists (<see cref="Backup.GoalsExport"/>, <see cref="ImportAsync"/>), never as sealed blobs. The rules (Deep work, the
/// looser-change wait) are in <see cref="GoalsService"/>; this class keeps the lists whole on every change: Deep work one and complete
/// (<see cref="GoalDefaults.Normalized"/>), no other goal built in, no live goal in a dead area (<see cref="GoalAreaTidy"/>), 30-day
/// tombstones pruned. ADR 79 forward compatibility: a list written by a newer version still opens; an item holding a value this version
/// doesn't know is kept as written (<see cref="GoalLists.Newer"/>) and unknown fields stay on their objects, both written back unchanged
/// on every save, and no change here removes them (<see cref="GoalUnknowns"/>). Use from the UI thread only.
/// </summary>
public sealed class GoalStore
{
    public const string ItemsKey = "goals.items";
    public const string AreasKey = "goals.areas";
    public const string EntriesKey = "goals.entries";
    public const string RemindersKey = "goals.reminders";
    public const string ReviewsKey = "goals.reviews";
    public const string DeepWorkKey = "goals.deepWork";

    /// <summary>Deep work's waiting changes: state of the moment, never in a backup (hard rule: backups hold no pending changes).</summary>
    public const string DeepWorkPendingKey = "goals.deepWork.pending";
    public const string UiKey = "goals.ui";
    public const string SettingsKey = "goals.settings";
    public const string MarkerKey = "goals.model";

    /// <summary>
    /// Round 2 R5.5: goal reminders' snoozed / dismissed / skipped occurrences on this PC (ids and times only; state of the moment, never
    /// synced, never in a backup).
    /// </summary>
    public const string ReminderStateKey = "goals.reminderState";

    /// <summary>Stored when a migration attempt of a model version made its database backup, so a failing one backs up once, not on every start.</summary>
    public const string AttemptKey = "goals.modelAttempt";

    /// <summary>The model the marker names; a later model change migrates again from this one.</summary>
    public const int ModelVersion = 1;

    /// <summary>The sealed list documents, in the order they are written.</summary>
    public static IReadOnlyList<string> SealedKeys { get; } = [ItemsKey, AreasKey, EntriesKey, RemindersKey, ReviewsKey];

    /// <summary>How long a deleted item's tombstone (no text) is kept, as for shared task items (ADR 48).</summary>
    public static readonly TimeSpan TombstoneKept = TimeSpan.FromDays(30);

    private readonly SqliteJsonDocumentStore _store;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _time;
    private GoalLists _lists = GoalLists.Empty;

    // Saves run one after another, so an older list never overwrites a newer one.
    private Task _saving = Task.CompletedTask;

    public GoalStore(SqliteJsonDocumentStore store, IDataProtector protector, TimeProvider time)
    {
        _store = store;
        _protector = protector;
        _time = time;
    }

    /// <summary>Set by the startup step (<see cref="GoalsMigration"/>); only <see cref="GoalModelState.Ready"/> reads and saves.</summary>
    public GoalModelState State { get; internal set; } = GoalModelState.NotMigrated;

    public bool IsReady => State == GoalModelState.Ready;

    public GoalLists Lists => _lists;

    /// <summary>Deep work's values in memory: the device setting and the waiting changes, stored in two documents.</summary>
    public DeepWorkGoalSettings DeepWork { get; private set; } = DeepWorkGoalSettings.Default;

    public GoalUiPreferences Ui { get; private set; } = GoalUiPreferences.Default;

    public GoalPageSettings PageSettings { get; private set; } = GoalPageSettings.Default;

    /// <summary>Goal reminders' occurrence marks on this PC (<see cref="GoalReminderSource"/>); empty when none or unreadable.</summary>
    public GoalReminderState ReminderState { get; private set; } = GoalReminderState.Empty;

    /// <summary>Raised after any change was kept (lists, Deep work's values, the page's preferences).</summary>
    public event EventHandler<GoalStoreChangedEventArgs>? Changed;

    /// <summary>How the document store writes plain documents (camelCase); a backup's copy of one reads with the same.</summary>
    public static JsonSerializerOptions DocumentJson { get; } = new(JsonSerializerDefaults.Web);

    private DateTimeOffset Now => _time.GetUtcNow();

    /// <summary>Whether the migration ran (its marker is stored).</summary>
    public bool HasMarkerBlocking()
    {
        return Task.Run(() => _store.LoadAsync<Marker>(MarkerKey)).GetAwaiter().GetResult() is { Model: >= ModelVersion };
    }

    /// <summary>Whether this model version's migration attempt already wrote its backup.</summary>
    public bool HasAttemptedBlocking()
    {
        return Task.Run(() => _store.LoadAsync<Marker>(AttemptKey)).GetAwaiter().GetResult() is { Model: >= ModelVersion };
    }

    /// <summary>Records that this model version's migration attempt wrote its backup.</summary>
    public void RecordAttemptBlocking()
    {
        Task.Run(() => _store.SaveAsync(AttemptKey, new Marker(ModelVersion))).GetAwaiter().GetResult();
    }

    /// <summary>
    /// The lists already stored without a marker (none expected: they are written with the marker in one transaction), so the first run
    /// builds on them rather than over them; null when none is stored. Throws when one can't be opened (never written over blind).
    /// </summary>
    public GoalLists? ReadUnmarkedBlocking()
    {
        bool any = SealedKeys.Any(key => Task.Run(() => _store.LoadAsync<JsonElement?>(key)).GetAwaiter().GetResult() is not null);
        return any ? ReadLists() : null;
    }

    /// <summary>Reads everything after a migration; false (and nothing kept) when a list or Deep work's values can't be read.</summary>
    public bool LoadBlocking()
    {
        try
        {
            // Deep work's values first: their one-time move of the waiting changes runs even when a list can't be read. They are part of
            // the rule (a weekly target, a waiting change): unreadable is never taken as "none".
            DeepWorkGoalSettings deepWork = ReadDeepWork();
            GoalLists lists = ReadLists();
            _lists = Normalize(lists, lists, Now);
            DeepWork = deepWork;
            Ui = LoadDisplay(UiKey, GoalUiPreferences.Default).Clamped();
            PageSettings = LoadDisplay(SettingsKey, GoalPageSettings.Default).Clamped();
            ReminderState = GoalReminderState.Whole(LoadDisplay(ReminderStateKey, GoalReminderState.Empty));
            return true;
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or FormatException or NotSupportedException or CryptographicException)
        {
            _lists = GoalLists.Empty;
            DeepWork = DeepWorkGoalSettings.Default;
            return false;
        }
    }

    /// <summary>
    /// Makes the first run's lists the goals: the five documents and the marker in one transaction, so a run that stops halfway leaves
    /// neither and the next start tries again.
    /// </summary>
    public void ActivateBlocking(GoalLists lists)
    {
        GoalLists normalized = Normalize(lists, lists, Now);
        // Read before the marker is written: a Deep work document that can't be read stops the run with no marker.
        DeepWorkGoalSettings deepWork = ReadDeepWork();
        Task.Run(() => SaveListsAsync(normalized, withMarker: true)).GetAwaiter().GetResult();
        _lists = normalized;
        DeepWork = deepWork;
        Ui = LoadDisplay(UiKey, GoalUiPreferences.Default).Clamped();
        PageSettings = LoadDisplay(SettingsKey, GoalPageSettings.Default).Clamped();
        ReminderState = GoalReminderState.Whole(LoadDisplay(ReminderStateKey, GoalReminderState.Empty));
    }

    /// <summary>
    /// A change to the lists: <paramref name="change"/> gets them and the time and returns the new lists, which are kept whole (Deep work,
    /// areas, tombstones), saved in one transaction and announced. Does nothing unless the goals are ready.
    /// </summary>
    /// <returns>Whether the lists changed.</returns>
    public async Task<bool> ChangeAsync(object? source, Func<GoalLists, DateTimeOffset, GoalLists> change, bool fromBackup = false)
    {
        if (!IsReady)
        {
            return false;
        }

        DateTimeOffset now = Now;
        GoalLists before = _lists;
        GoalLists after = Normalize(before, change(before, now), now);
        if (Signature(after) == Signature(before))
        {
            // Nothing to save: an earlier save's failure belongs to the change that asked for it, not to this one.
            return false;
        }

        _lists = after;
        Task save = QueueSave(after);
        Changed?.Invoke(this, new GoalStoreChangedEventArgs(source, fromBackup));
        await save;
        return true;
    }

    /// <summary>
    /// Saves Deep work's values (the rules decide them in <see cref="GoalsService"/>, one change at a time): the weekly target and Track
    /// time only in "goals.deepWork", the waiting changes in "goals.deepWork.pending", in one transaction. Does nothing unless ready.
    /// </summary>
    public async Task SetDeepWorkAsync(DeepWorkGoalSettings settings, object? source = null)
    {
        if (!IsReady || settings == DeepWork)
        {
            return;
        }

        DeepWork = settings;
        Task save = Queue(() => SaveDeepWorkAsync(settings));
        Changed?.Invoke(this, new GoalStoreChangedEventArgs(source, false));
        await save;
    }

    /// <summary>The page's UI state (display only: at once).</summary>
    public async Task SetUiAsync(GoalUiPreferences preferences, object? source = null)
    {
        GoalUiPreferences clamped = preferences.Clamped();
        if (!IsReady || Same(clamped, Ui))
        {
            return;
        }

        Ui = clamped;
        Task save = Queue(() => _store.SaveAsync(UiKey, clamped));
        Changed?.Invoke(this, new GoalStoreChangedEventArgs(source, false));
        await save;
    }

    /// <summary>Options › Charts (display and period maths only: at once).</summary>
    public async Task SetPageSettingsAsync(GoalPageSettings settings, object? source = null)
    {
        GoalPageSettings clamped = settings.Clamped();
        if (!IsReady || clamped == PageSettings)
        {
            return;
        }

        PageSettings = clamped;
        Task save = Queue(() => _store.SaveAsync(SettingsKey, clamped));
        Changed?.Invoke(this, new GoalStoreChangedEventArgs(source, false));
        await save;
    }

    /// <summary>
    /// Goal reminders' occurrence marks (round 2 R5.5): kept at once in memory, then saved. State only: no <see cref="Changed"/> (the goal
    /// lists didn't change; <see cref="GoalReminderSource"/> looks again itself). Does nothing unless ready; a failed save is forgotten
    /// (at worst a reminder rings again after a restart).
    /// </summary>
    public async Task SetReminderStateAsync(GoalReminderState state)
    {
        if (!IsReady)
        {
            return;
        }

        ReminderState = state;
        try
        {
            await Queue(() => _store.SaveAsync(ReminderStateKey, state));
        }
        catch (SqliteException)
        {
            // Rung again after a restart at worst.
        }
    }

    /// <summary>
    /// A backup's goals (ADR 71, with the task lists) taken in through the same guards as the screens: areas first; Deep work keeps its
    /// place, sources and targets (only its name, icon, colour, Show on Today, extra tiles and notes come in) and takes no entries; no
    /// other goal comes in built in, or with a source that isn't allowed now ("Coming soon", all focus); a goal whose area isn't there
    /// goes to "Work". With <paramref name="replace"/> the backup's live items are stamped now and win; otherwise the newest edit wins.
    /// The backup's deletions are never taken. A backup made without a password has no notes or reasons
    /// (<paramref name="includesNotes"/> false): this PC's stay. Re-sealed with this PC's key when saved. Returns a note for the summary.
    /// </summary>
    public async Task<string> ImportAsync(GoalLists backup, bool replace, bool includesNotes)
    {
        if (!IsReady)
        {
            return "Goals weren't restored: this PC's goals aren't ready yet.";
        }

        int live = backup.Goals.Count(goal => goal.IsLive && !goal.IsDeepWork);
        await ChangeAsync(null, (lists, now) => Imported(lists, backup, replace, includesNotes, now), fromBackup: true);
        return replace ? $"Goals restored ({live} from the backup)." : $"Goals merged ({live} in the backup; the newest edit of each kept).";
    }

    /// <summary>
    /// One sealed list from a backup made by the first goals build (device settings, DPAPI blob; plan R4 review): opened with this PC's
    /// key and taken in like <see cref="ImportAsync"/> (restore). A blob sealed on another PC or Windows account, or a damaged one, leaves
    /// this PC's goals as they are, with a note saying which.
    /// </summary>
    public async Task<string?> RestoreListAsync(string key, JsonElement value)
    {
        if (!IsReady)
        {
            return "Goals weren't restored: this PC's goals aren't ready yet.";
        }

        (string? json, bool damaged) = Unseal(value);
        if (json is null)
        {
            return damaged
                ? "Goals in this backup are damaged and couldn't be read; this PC's goals were kept."
                : "Goals in this backup were sealed on another PC or Windows account and can't be opened here; this PC's goals were kept.";
        }

        try
        {
            GoalLists partial = key switch
            {
                ItemsKey => GoalLists.Empty with { Goals = Parse<Goal>(json).Known },
                AreasKey => GoalLists.Empty with { Areas = Parse<GoalArea>(json).Known },
                EntriesKey => GoalLists.Empty with { Entries = Parse<GoalEntry>(json).Known },
                RemindersKey => GoalLists.Empty with { Reminders = Parse<GoalReminder>(json).Known },
                ReviewsKey => GoalLists.Empty with { Reviews = Parse<GoalReviewRecord>(json).Known },
                _ => GoalLists.Empty,
            };
            string note = await ImportAsync(partial, replace: true, includesNotes: true);
            return key is ItemsKey ? note : null;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return "Goals in this backup are damaged and couldn't be read; this PC's goals were kept.";
        }
    }

    /// <summary>The page's UI state or Options from a backup (display only: taken as they are).</summary>
    public async Task<string?> RestoreDisplayAsync(string key, JsonElement value)
    {
        if (!IsReady)
        {
            return null;
        }

        try
        {
            if (key == UiKey && value.Deserialize<GoalUiPreferences>(DocumentJson) is { } ui)
            {
                await SetUiAsync(ui);
            }
            else if (key == SettingsKey && value.Deserialize<GoalPageSettings>(DocumentJson) is { } settings)
            {
                await SetPageSettingsAsync(settings);
            }
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Display preferences only: an unreadable copy leaves this PC's.
        }

        return null;
    }

    // Deep work stays whole and stays one; no other goal is built in; a live goal never sits in a dead area (it goes to "Work", which
    // comes back if needed: GoalAreaTidy, the phone's rule too, stamped from the lists, never the clock); tombstones older than
    // TombstoneKept go. A change can never take Deep work away, nor what this version doesn't know (ADR 79): unknown fields, a goal's
    // newer source rules, a review's newer decisions and each list's newer items come back from before when the change left them out.
    private static GoalLists Normalize(GoalLists before, GoalLists after, DateTimeOffset now)
    {
        DateTimeOffset cutoff = now - TombstoneKept;
        List<Goal> goals = after.Goals.Where(goal => goal.Id != Goal.DeepWorkId).Select(goal => goal.BuiltIn ? goal with { BuiltIn = false } : goal).ToList();
        Goal? deepWork = after.Goals.FirstOrDefault(goal => goal.Id == Goal.DeepWorkId) ?? before.Goals.FirstOrDefault(goal => goal.Id == Goal.DeepWorkId);
        if (deepWork is not null)
        {
            goals.Insert(0, GoalDefaults.Normalized(deepWork));
        }

        (IReadOnlyList<GoalArea> areas, IReadOnlyList<Goal> placed) = GoalAreaTidy.Rehome(after.Areas, goals, GoalDefaults.NewWorkArea(now));
        var tidied = new GoalLists(
            areas.Where(area => area.DeletedAt is not DateTimeOffset at || at >= cutoff).ToList(),
            placed.Where(goal => goal.DeletedAt is not DateTimeOffset at || at >= cutoff).ToList(),
            after.Entries.Where(entry => entry.DeletedAt is not DateTimeOffset at || at >= cutoff).ToList(),
            after.Reminders.Where(reminder => reminder.DeletedAt is not DateTimeOffset at || at >= cutoff).ToList(),
            after.Reviews.Where(review => review.DeletedAt is not DateTimeOffset at || at >= cutoff).ToList())
        {
            Newer = after.Newer,
        };
        return KeepUnknowns(before, tidied);
    }

    // ADR 79: what this version doesn't know stays (GoalUnknowns), whatever a change rebuilt.
    private static GoalLists KeepUnknowns(GoalLists before, GoalLists after)
    {
        return new GoalLists(
            GoalUnknowns.Keep(before.Areas, after.Areas),
            GoalUnknowns.Keep(before.Goals, after.Goals),
            GoalUnknowns.Keep(before.Entries, after.Entries),
            GoalUnknowns.Keep(before.Reminders, after.Reminders),
            GoalUnknowns.Keep(before.Reviews, after.Reviews))
        {
            Newer = new GoalNewerLists(
                GoalUnknowns.KeepNewer(before.Newer.Areas, after.Newer.Areas),
                GoalUnknowns.KeepNewer(before.Newer.Goals, after.Newer.Goals),
                GoalUnknowns.KeepNewer(before.Newer.Entries, after.Newer.Entries),
                GoalUnknowns.KeepNewer(before.Newer.Reminders, after.Newer.Reminders),
                GoalUnknowns.KeepNewer(before.Newer.Reviews, after.Newer.Reviews)),
        };
    }

    private static GoalLists Imported(GoalLists lists, GoalLists backup, bool replace, bool includesNotes, DateTimeOffset now)
    {
        // Restore: the backup's items stamped now, so they win over later edits; merge: they keep their own edit time.
        DateTimeOffset Stamp(DateTimeOffset edited) => replace ? now : edited;

        // Areas first, so the goals below find theirs (Normalize sends any that still can't to "Work").
        List<GoalArea> areas = Merge(lists.Areas, backup.Areas, area => area.Id, area => area.IsLive, area => area.EditedAt, replace,
            (incoming, _) => incoming with { EditedAt = Stamp(incoming.EditedAt) });

        Goal? ownDeepWork = lists.Goals.FirstOrDefault(goal => goal.Id == Goal.DeepWorkId);
        List<Goal> incomingGoals = backup.Goals
            .Where(goal => !goal.IsDeepWork)
            .Select(goal => goal with
            {
                Sources = goal.Sources.Where(rule => GoalSources.AllowedFor(goal, rule.Type)).ToList(),
                // Other goals feed no guard: no waiting change and no second target come in with them (Deep work's are its own).
                SecondTarget = null,
                Target = goal.Target with { PendingChange = null },
            })
            .ToList();
        List<Goal> goals = Merge(lists.Goals, incomingGoals, goal => goal.Id, goal => goal.IsLive, goal => goal.EditedAt, replace,
            (incoming, held) => incoming with { EditedAt = Stamp(incoming.EditedAt), Notes = includesNotes ? incoming.Notes : held?.Notes });
        if (ownDeepWork is not null && backup.Goals.FirstOrDefault(goal => goal.Id == Goal.DeepWorkId && goal.IsLive) is { } backupDeepWork
            && (replace || backupDeepWork.EditedAt > ownDeepWork.EditedAt))
        {
            // Only what the user may change on Deep work; its place, sources and targets stay this PC's.
            goals[goals.FindIndex(goal => goal.Id == Goal.DeepWorkId)] = ownDeepWork with
            {
                Name = backupDeepWork.Name,
                Icon = backupDeepWork.Icon,
                Color = backupDeepWork.Color,
                ShowOnToday = backupDeepWork.ShowOnToday,
                ExtraTiles = backupDeepWork.ExtraTiles,
                Notes = includesNotes ? backupDeepWork.Notes : ownDeepWork.Notes,
                EditedAt = now,
            };
        }

        // An entry or a reminder comes in only for a goal that is live after the merge (never for a deleted or unknown goal); Deep work
        // counts focus only, so no entry comes in for it.
        HashSet<Guid> liveGoals = goals.Where(goal => goal.IsLive).Select(goal => goal.Id).ToHashSet();
        List<GoalEntry> entries = Merge(lists.Entries, backup.Entries.Where(entry => entry.GoalId != Goal.DeepWorkId && liveGoals.Contains(entry.GoalId)).ToList(),
            entry => entry.Id, entry => entry.IsLive, entry => entry.EditedAt, replace, (incoming, _) => incoming with { EditedAt = Stamp(incoming.EditedAt) });
        List<GoalReminder> reminders = Merge(lists.Reminders, backup.Reminders.Where(reminder => liveGoals.Contains(reminder.GoalId)).ToList(), reminder => reminder.Id, reminder => reminder.IsLive,
            reminder => reminder.EditedAt, replace, (incoming, held) => incoming with { EditedAt = Stamp(incoming.EditedAt), Reason = includesNotes ? incoming.Reason : held?.Reason });
        List<GoalReviewRecord> reviews = Merge(lists.Reviews, backup.Reviews, review => review.Id, review => review.IsLive,
            review => review.EditedAt, replace, (incoming, held) => incoming with { EditedAt = Stamp(incoming.EditedAt), Notes = includesNotes ? incoming.Notes : held?.Notes });
        return new GoalLists(areas, goals, entries, reminders, reviews);
    }

    // The backup's live items come in (stamped now by take); with replace they always win, otherwise only over an older edit. This PC's
    // other items stay; the backup's tombstones are ignored (an old backup never deletes).
    private static List<T> Merge<T>(
        IReadOnlyList<T> current, IReadOnlyList<T> backup, Func<T, Guid> id, Func<T, bool> live, Func<T, DateTimeOffset> editedAt, bool replace,
        Func<T, T?, T> take)
        where T : class
    {
        var merged = current.ToList();
        foreach (T incoming in backup.Where(live).GroupBy(id).Select(group => group.First()))
        {
            int index = merged.FindIndex(item => id(item) == id(incoming));
            T? held = index < 0 ? null : merged[index];
            if (held is not null && !replace && editedAt(held) >= editedAt(incoming))
            {
                continue;
            }

            if (index < 0)
            {
                merged.Add(take(incoming, null));
            }
            else
            {
                merged[index] = take(incoming, held);
            }
        }

        return merged;
    }

    private GoalLists ReadLists()
    {
        (List<GoalArea> areas, List<GoalNewerItem> newerAreas) = LoadList<GoalArea>(AreasKey);
        (List<Goal> goals, List<GoalNewerItem> newerGoals) = LoadList<Goal>(ItemsKey);
        (List<GoalEntry> entries, List<GoalNewerItem> newerEntries) = LoadList<GoalEntry>(EntriesKey);
        (List<GoalReminder> reminders, List<GoalNewerItem> newerReminders) = LoadList<GoalReminder>(RemindersKey);
        (List<GoalReviewRecord> reviews, List<GoalNewerItem> newerReviews) = LoadList<GoalReviewRecord>(ReviewsKey);
        return new GoalLists(areas, goals, entries, reminders, reviews)
        {
            Newer = new GoalNewerLists(newerAreas, newerGoals, newerEntries, newerReminders, newerReviews),
        };
    }

    // The device setting and the waiting changes. The first goals build kept the waiting changes in "goals.deepWork"; they move to their
    // state document once (both written in one transaction), so a backup of the device setting never holds one.
    private DeepWorkGoalSettings ReadDeepWork()
    {
        DeepWorkGoalSettings stored = Task.Run(() => _store.LoadAsync<DeepWorkGoalSettings>(DeepWorkKey)).GetAwaiter().GetResult()
            ?? DeepWorkGoalSettings.Default;
        StoredPending? pending = Task.Run(() => _store.LoadAsync<StoredPending>(DeepWorkPendingKey)).GetAwaiter().GetResult();
        if (pending is not null)
        {
            return stored with { PendingDaily = pending.PendingDaily, PendingWeekly = pending.PendingWeekly, PendingTrackTimeOnly = pending.PendingTrackTimeOnly };
        }

        if (stored.HasPending)
        {
            Task.Run(() => SaveDeepWorkAsync(stored)).GetAwaiter().GetResult();
        }

        return stored;
    }

    private Task SaveDeepWorkAsync(DeepWorkGoalSettings settings)
    {
        return _store.SaveAllAsync(
        [
            new(DeepWorkKey, new StoredDeepWork(settings.WeeklyMinutes, settings.TrackTimeOnly)),
            new(DeepWorkPendingKey, new StoredPending(settings.PendingDaily, settings.PendingWeekly, settings.PendingTrackTimeOnly)),
        ]);
    }

    private Task QueueSave(GoalLists lists)
    {
        return Queue(() => SaveListsAsync(lists));
    }

    // After the saves asked for before it, whatever became of them; a failed save still fails the task it returns.
    private Task Queue(Func<Task> save)
    {
        _saving = _saving.ContinueWith(_ => save(), TaskScheduler.Default).Unwrap();
        return _saving;
    }

    // One transaction: a goal and its entries, reminders and area always agree.
    private Task SaveListsAsync(GoalLists lists, bool withMarker = false)
    {
        var documents = new List<KeyValuePair<string, object>>
        {
            new(ItemsKey, Sealed(lists.Goals, lists.Newer.Goals)),
            new(AreasKey, Sealed(lists.Areas, lists.Newer.Areas)),
            new(EntriesKey, Sealed(lists.Entries, lists.Newer.Entries)),
            new(RemindersKey, Sealed(lists.Reminders, lists.Newer.Reminders)),
            new(ReviewsKey, Sealed(lists.Reviews, lists.Newer.Reviews)),
        };
        if (withMarker)
        {
            documents.Add(new(MarkerKey, new Marker(ModelVersion)));
        }

        return _store.SaveAllAsync(documents);
    }

    // A list with its newer items back at their places (ADR 79); with none, the same text as before.
    private Stored Sealed<T>(IReadOnlyList<T> known, IReadOnlyList<GoalNewerItem> newer)
    {
        return new Stored(Convert.ToBase64String(_protector.Protect(GoalNewerJson.WriteList(known, newer, SyncJson.Options))));
    }

    private (List<T> Known, List<GoalNewerItem> Newer) LoadList<T>(string key)
    {
        Stored? stored = Task.Run(() => _store.LoadAsync<Stored>(key)).GetAwaiter().GetResult();
        if (stored is null)
        {
            return ([], []);
        }

        string json = _protector.Unprotect(Convert.FromBase64String(stored.Data))
            ?? throw new FormatException("A goals document couldn't be opened.");
        return Parse<T>(json);
    }

    // ADR 79: an item holding a value this version doesn't know is kept as written, never a reason to close the goals; anything else
    // that can't be read still is (JsonException).
    private static (List<T> Known, List<GoalNewerItem> Newer) Parse<T>(string json)
    {
        return GoalNewerJson.ReadList<T>(json, SyncJson.Options);
    }

    // A backup's copy of a sealed document: its JSON, or why not. Damaged = not a sealed document or not base64 (this PC's protector
    // returns null for a blob it can't open, whether another PC's or a corrupted one, so those read as "another PC").
    private (string? Json, bool Damaged) Unseal(JsonElement value)
    {
        try
        {
            if (value.Deserialize<Stored>(DocumentJson) is not { Data: { Length: > 0 } data })
            {
                return (null, true);
            }

            return (_protector.Unprotect(Convert.FromBase64String(data)), false);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or NotSupportedException)
        {
            return (null, true);
        }
        catch (CryptographicException)
        {
            return (null, false);
        }
    }

    // Display preferences: one that can't be read starts from the defaults (it loosens nothing).
    private T LoadDisplay<T>(string key, T fallback)
        where T : class
    {
        try
        {
            return Task.Run(() => _store.LoadAsync<T>(key)).GetAwaiter().GetResult() ?? fallback;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return fallback;
        }
    }

    private static bool Same(GoalUiPreferences left, GoalUiPreferences right)
    {
        return left.ListWidth == right.ListWidth && left.ListView == right.ListView && left.Sort == right.Sort
            && (left.CollapsedAreas ?? []).SequenceEqual(right.CollapsedAreas ?? []);
    }

    private static string Signature(GoalLists lists)
    {
        return JsonSerializer.Serialize(lists, SyncJson.Options);
    }

    private sealed record Stored(string Data);

    private sealed record Marker(int Model);

    // "goals.deepWork" as stored: the device setting only.
    private sealed record StoredDeepWork(int? WeeklyMinutes, bool TrackTimeOnly);

    // "goals.deepWork.pending": the waiting changes (state).
    private sealed record StoredPending(GoalPendingChange? PendingDaily, GoalPendingChange? PendingWeekly, GoalPendingChange? PendingTrackTimeOnly);
}
