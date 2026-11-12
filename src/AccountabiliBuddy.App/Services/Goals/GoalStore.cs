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
/// plus three plain device-local documents: Deep work's weekly target and waiting changes ("goals.deepWork", <see cref="DeepWorkGoalSettings"/>),
/// the page's UI state ("goals.ui") and its Options › Charts ("goals.settings"). Nothing here syncs until ADR 79 is accepted; the items
/// keep their ids, positions, edit stamps and tombstones so turning sync on later is a merge. Names, notes and reasons are private
/// text: sealed at rest, never logged. The rules (Deep work, the looser-change wait) are in <see cref="GoalsService"/>; this class only
/// keeps Deep work's record whole (<see cref="GoalDefaults.Normalized"/>) and prunes 30-day tombstones. Use from the UI thread only.
/// </summary>
public sealed class GoalStore
{
    public const string ItemsKey = "goals.items";
    public const string AreasKey = "goals.areas";
    public const string EntriesKey = "goals.entries";
    public const string RemindersKey = "goals.reminders";
    public const string ReviewsKey = "goals.reviews";
    public const string DeepWorkKey = "goals.deepWork";
    public const string UiKey = "goals.ui";
    public const string SettingsKey = "goals.settings";
    public const string MarkerKey = "goals.model";

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

    public DeepWorkGoalSettings DeepWork { get; private set; } = DeepWorkGoalSettings.Default;

    public GoalUiPreferences Ui { get; private set; } = GoalUiPreferences.Default;

    public GoalPageSettings PageSettings { get; private set; } = GoalPageSettings.Default;

    /// <summary>Raised after any change was kept (lists, Deep work's values, the page's preferences).</summary>
    public event EventHandler<GoalStoreChangedEventArgs>? Changed;

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
            GoalLists lists = ReadLists();
            // Deep work's values are part of the rule (a weekly target, a waiting change): unreadable is never taken as "none".
            DeepWorkGoalSettings deepWork = Task.Run(() => _store.LoadAsync<DeepWorkGoalSettings>(DeepWorkKey)).GetAwaiter().GetResult()
                ?? DeepWorkGoalSettings.Default;
            _lists = Normalize(lists, lists, Now);
            DeepWork = deepWork;
            Ui = LoadDisplay(UiKey, GoalUiPreferences.Default).Clamped();
            PageSettings = LoadDisplay(SettingsKey, GoalPageSettings.Default).Clamped();
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
        DeepWorkGoalSettings deepWork = Task.Run(() => _store.LoadAsync<DeepWorkGoalSettings>(DeepWorkKey)).GetAwaiter().GetResult()
            ?? DeepWorkGoalSettings.Default;
        Task.Run(() => SaveListsAsync(normalized, withMarker: true)).GetAwaiter().GetResult();
        _lists = normalized;
        DeepWork = deepWork;
        Ui = LoadDisplay(UiKey, GoalUiPreferences.Default).Clamped();
        PageSettings = LoadDisplay(SettingsKey, GoalPageSettings.Default).Clamped();
    }

    /// <summary>
    /// A change to the lists: <paramref name="change"/> gets them and the time and returns the new lists, which are kept whole (Deep work,
    /// tombstones), saved in one transaction and announced. Does nothing unless the goals are ready.
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
            await _saving;
            return false;
        }

        _lists = after;
        Task save = QueueSave(after);
        Changed?.Invoke(this, new GoalStoreChangedEventArgs(source, fromBackup));
        await save;
        return true;
    }

    /// <summary>Saves Deep work's values (the rules decide them in <see cref="GoalsService"/>). Does nothing unless the goals are ready.</summary>
    public async Task SetDeepWorkAsync(DeepWorkGoalSettings settings, object? source = null)
    {
        if (!IsReady || settings == DeepWork)
        {
            return;
        }

        DeepWork = settings;
        Task save = Queue(() => _store.SaveAsync(DeepWorkKey, settings));
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
    /// One sealed list from a backup's device settings (ADR 71): opened with this PC's key and merged in, the backup's live items stamped
    /// now so they come back over later edits and deletions; items made since stay and the backup's deletions are not taken. Deep work
    /// takes only its name, icon, colour, Show on Today, extra tiles and notes from it. A list sealed on another PC or Windows account
    /// can't be opened: this PC's goals stay as they are. Returns a note for the restore summary, or null.
    /// </summary>
    public async Task<string?> RestoreListAsync(string key, JsonElement value)
    {
        if (!IsReady)
        {
            return "Goals weren't restored: this PC's goals aren't ready yet.";
        }

        string? json = Unseal(value);
        if (json is null)
        {
            return "Goals in this backup were sealed on another PC or Windows account and can't be opened here; this PC's goals were kept.";
        }

        try
        {
            int restored = 0;
            await ChangeAsync(null, (lists, now) =>
            {
                switch (key)
                {
                    case ItemsKey:
                        List<Goal> goals = Parse<Goal>(json);
                        restored = goals.Count(goal => goal.IsLive);
                        return lists with { Goals = MergeGoals(lists.Goals, goals, now) };
                    case AreasKey:
                        List<GoalArea> areas = Parse<GoalArea>(json);
                        restored = areas.Count(area => area.IsLive);
                        return lists with { Areas = Merge(lists.Areas, areas, area => area.Id, area => area.IsLive, area => area with { EditedAt = now }) };
                    case EntriesKey:
                        return lists with { Entries = Merge(lists.Entries, Parse<GoalEntry>(json), entry => entry.Id, entry => entry.IsLive, entry => entry with { EditedAt = now }) };
                    case RemindersKey:
                        return lists with { Reminders = Merge(lists.Reminders, Parse<GoalReminder>(json), reminder => reminder.Id, reminder => reminder.IsLive, reminder => reminder with { EditedAt = now }) };
                    case ReviewsKey:
                        return lists with { Reviews = Merge(lists.Reviews, Parse<GoalReviewRecord>(json), review => review.Id, review => review.IsLive, review => review with { EditedAt = now }) };
                    default:
                        return lists;
                }
            }, fromBackup: true);
            return key switch
            {
                ItemsKey => $"Goals restored ({restored} from the backup).",
                AreasKey => $"Goal areas restored ({restored} from the backup).",
                _ => null,
            };
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return "Goals in this backup couldn't be read; this PC's goals were kept.";
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

    /// <summary>How the document store writes plain documents (camelCase); a backup's copy of one reads with the same.</summary>
    public static JsonSerializerOptions DocumentJson { get; } = new(JsonSerializerDefaults.Web);

    // Deep work stays whole and stays one; tombstones older than TombstoneKept go. A change can never take Deep work away.
    private static GoalLists Normalize(GoalLists before, GoalLists after, DateTimeOffset now)
    {
        DateTimeOffset cutoff = now - TombstoneKept;
        List<Goal> goals = after.Goals.Where(goal => goal.Id != Goal.DeepWorkId).ToList();
        Goal? deepWork = after.Goals.FirstOrDefault(goal => goal.Id == Goal.DeepWorkId) ?? before.Goals.FirstOrDefault(goal => goal.Id == Goal.DeepWorkId);
        if (deepWork is not null)
        {
            goals.Insert(0, GoalDefaults.Normalized(deepWork));
        }

        return new GoalLists(
            after.Areas.Where(area => area.DeletedAt is not DateTimeOffset at || at >= cutoff).ToList(),
            goals.Where(goal => goal.DeletedAt is not DateTimeOffset at || at >= cutoff).ToList(),
            after.Entries.Where(entry => entry.DeletedAt is not DateTimeOffset at || at >= cutoff).ToList(),
            after.Reminders.Where(reminder => reminder.DeletedAt is not DateTimeOffset at || at >= cutoff).ToList(),
            after.Reviews.Where(review => review.DeletedAt is not DateTimeOffset at || at >= cutoff).ToList());
    }

    private static List<Goal> MergeGoals(IReadOnlyList<Goal> current, IReadOnlyList<Goal> backup, DateTimeOffset now)
    {
        Goal? ownDeepWork = current.FirstOrDefault(goal => goal.Id == Goal.DeepWorkId);
        List<Goal> merged = Merge(current, backup.Where(goal => goal.Id != Goal.DeepWorkId).ToList(), goal => goal.Id, goal => goal.IsLive, goal => goal with { EditedAt = now });
        if (ownDeepWork is not null && backup.FirstOrDefault(goal => goal.Id == Goal.DeepWorkId && goal.IsLive) is { } backupDeepWork)
        {
            // Only what the user may change on Deep work; its place, sources and targets stay this PC's.
            Goal restored = ownDeepWork with
            {
                Name = backupDeepWork.Name,
                Icon = backupDeepWork.Icon,
                Color = backupDeepWork.Color,
                ShowOnToday = backupDeepWork.ShowOnToday,
                ExtraTiles = backupDeepWork.ExtraTiles,
                Notes = backupDeepWork.Notes,
                EditedAt = now,
            };
            merged[merged.FindIndex(goal => goal.Id == Goal.DeepWorkId)] = restored;
        }

        return merged;
    }

    // The backup's live items replace this PC's copies (stamped now); this PC's other items stay; the backup's tombstones are ignored.
    private static List<T> Merge<T>(IReadOnlyList<T> current, IReadOnlyList<T> backup, Func<T, Guid> id, Func<T, bool> live, Func<T, T> stamp)
    {
        Dictionary<Guid, T> restored = backup.Where(live).GroupBy(id).ToDictionary(group => group.Key, group => stamp(group.First()));
        var merged = current.Select(item => restored.Remove(id(item), out T? fromBackup) ? fromBackup! : item).ToList();
        merged.AddRange(restored.Values);
        return merged;
    }

    private GoalLists ReadLists()
    {
        return new GoalLists(
            LoadList<GoalArea>(AreasKey),
            LoadList<Goal>(ItemsKey),
            LoadList<GoalEntry>(EntriesKey),
            LoadList<GoalReminder>(RemindersKey),
            LoadList<GoalReviewRecord>(ReviewsKey));
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
            new(ItemsKey, Sealed(lists.Goals)),
            new(AreasKey, Sealed(lists.Areas)),
            new(EntriesKey, Sealed(lists.Entries)),
            new(RemindersKey, Sealed(lists.Reminders)),
            new(ReviewsKey, Sealed(lists.Reviews)),
        };
        if (withMarker)
        {
            documents.Add(new(MarkerKey, new Marker(ModelVersion)));
        }

        return _store.SaveAllAsync(documents);
    }

    private Stored Sealed<T>(T value)
    {
        return new Stored(Convert.ToBase64String(_protector.Protect(JsonSerializer.Serialize(value, SyncJson.Options))));
    }

    private List<T> LoadList<T>(string key)
    {
        Stored? stored = Task.Run(() => _store.LoadAsync<Stored>(key)).GetAwaiter().GetResult();
        if (stored is null)
        {
            return [];
        }

        string json = _protector.Unprotect(Convert.FromBase64String(stored.Data))
            ?? throw new FormatException("A goals document couldn't be opened.");
        return Parse<T>(json);
    }

    private static List<T> Parse<T>(string json)
    {
        return (JsonSerializer.Deserialize<List<T>>(json, SyncJson.Options) ?? []).Where(item => item is not null).ToList();
    }

    // A backup's copy of a sealed document; null when this PC can't open it.
    private string? Unseal(JsonElement value)
    {
        try
        {
            if (value.Deserialize<Stored>(DocumentJson) is not { Data: { Length: > 0 } data })
            {
                return null;
            }

            return _protector.Unprotect(Convert.FromBase64String(data));
        }
        catch (Exception ex) when (ex is JsonException or FormatException or CryptographicException or NotSupportedException)
        {
            return null;
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
        return left.ListWidth == right.ListWidth && left.ListView == right.ListView
            && (left.CollapsedAreas ?? []).SequenceEqual(right.CollapsedAreas ?? []);
    }

    private static string Signature(GoalLists lists)
    {
        return JsonSerializer.Serialize(lists, SyncJson.Options);
    }

    private sealed record Stored(string Data);

    private sealed record Marker(int Model);
}
