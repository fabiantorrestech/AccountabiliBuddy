using System.Globalization;
using AccountabiliBuddy.Core.Goals;
using AccountabiliBuddy.Core.Storage;

namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>
/// The startup step that makes this PC's goals, once (brief §5.3, plan R4.4). With the marker ("goals.model") stored it only reads the
/// goals. Otherwise it copies the whole database first (a "goals" backup, once per model version), then makes the default area "Work"
/// and the built-in Deep work goal in it, and nothing else; Deep work's daily target stays the "goal" document (<see cref="GoalService"/>,
/// ADR 29 untouched) and it has no weekly target. The lists and the marker are written in one transaction, only when the result
/// balances (every item already there kept, exactly one built-in goal in a live area, no repeated id). Anything that goes wrong
/// leaves no marker: goals stay unavailable, everything else works as before, and the next start tries again. Running it again
/// changes nothing. The log ("goals-model.log" in the data folder) holds counts only, never names.
/// </summary>
public static class GoalsMigration
{
    public const string LogFileName = "goals-model.log";

    public const string BackupLabel = "goals";

    /// <returns>Where the goals stand from now on; also kept in <see cref="GoalStore.State"/>.</returns>
    public static GoalModelState RunBlocking(LocalDatabase database, GoalStore store, TimeProvider time, string dataDirectory)
    {
        GoalModelState state = Run(database, store, time, Path.Combine(dataDirectory, LogFileName));
        store.State = state;
        return state;
    }

    /// <summary>The first run's lists from what is stored (normally nothing): the Work area and Deep work added where missing.</summary>
    public static GoalLists Seed(GoalLists existing, DateTimeOffset now)
    {
        var areas = existing.Areas.ToList();
        var goals = existing.Goals.ToList();
        int workIndex = areas.FindIndex(area => area.Id == GoalDefaults.WorkAreaId);
        if (workIndex < 0)
        {
            areas.Insert(0, GoalDefaults.NewWorkArea(now));
        }
        else if (!areas[workIndex].IsLive)
        {
            // Deep work needs a live area: the default one comes back rather than a second "Work".
            areas[workIndex] = GoalDefaults.NewWorkArea(now) with { Position = areas[workIndex].Position };
        }

        int deepWorkIndex = goals.FindIndex(goal => goal.Id == Goal.DeepWorkId);
        if (deepWorkIndex < 0)
        {
            goals.Insert(0, GoalDefaults.NewDeepWork(GoalDefaults.WorkAreaId, now));
        }
        else
        {
            // Already there (not expected without a marker): kept, made whole, and in a live area.
            Goal deepWork = GoalDefaults.Normalized(goals[deepWorkIndex]);
            bool inLiveArea = areas.Any(area => area.IsLive && area.Id == deepWork.AreaId);
            goals[deepWorkIndex] = inLiveArea ? deepWork : deepWork with { AreaId = GoalDefaults.WorkAreaId };
        }

        return existing with { Areas = areas, Goals = goals };
    }

    /// <summary>
    /// Whether <paramref name="seeded"/> is <paramref name="existing"/> plus at most the Work area and Deep work: nothing lost, no id twice,
    /// exactly one live built-in goal (Deep work) and it sits in a live area.
    /// </summary>
    public static bool IsBalanced(GoalLists existing, GoalLists seeded)
    {
        bool hadWork = existing.Areas.Any(area => area.Id == GoalDefaults.WorkAreaId);
        bool hadDeepWork = existing.Goals.Any(goal => goal.Id == Goal.DeepWorkId);
        bool unique = Unique(seeded.Areas.Select(area => area.Id)) && Unique(seeded.Goals.Select(goal => goal.Id))
            && Unique(seeded.Entries.Select(entry => entry.Id)) && Unique(seeded.Reminders.Select(reminder => reminder.Id))
            && Unique(seeded.Reviews.Select(review => review.Id));
        bool kept = existing.Areas.All(area => seeded.Areas.Any(item => item.Id == area.Id))
            && existing.Goals.All(goal => seeded.Goals.Any(item => item.Id == goal.Id))
            && seeded.Areas.Count == existing.Areas.Count + (hadWork ? 0 : 1)
            && seeded.Goals.Count == existing.Goals.Count + (hadDeepWork ? 0 : 1)
            && seeded.Entries.Count == existing.Entries.Count
            && seeded.Reminders.Count == existing.Reminders.Count
            && seeded.Reviews.Count == existing.Reviews.Count;
        List<Goal> builtIn = seeded.Goals.Where(goal => goal.IsLive && goal.BuiltIn).ToList();
        bool oneDeepWork = builtIn.Count == 1 && builtIn[0].Id == Goal.DeepWorkId
            && seeded.Areas.Any(area => area.IsLive && area.Id == builtIn[0].AreaId);
        return unique && kept && oneDeepWork;
    }

    private static GoalModelState Run(LocalDatabase database, GoalStore store, TimeProvider time, string logPath)
    {
        bool migrated;
        try
        {
            migrated = store.HasMarkerBlocking();
        }
        catch (Exception ex)
        {
            // Whether the goals were made isn't known: they are never made again over what may be there.
            Log(logPath, time, "marker could not be read (" + ex.GetType().Name + "); goals closed");
            return GoalModelState.Unreadable;
        }

        try
        {
            if (migrated)
            {
                if (store.LoadBlocking())
                {
                    return GoalModelState.Ready;
                }

                Log(logPath, time, "goals could not be read; goals closed (never rebuilt on a guess)");
                return GoalModelState.Unreadable;
            }

            // One backup per attempt version: a migration that keeps failing must not copy the whole database on every start.
            if (store.HasAttemptedBlocking())
            {
                Log(logPath, time, "backup of this version's attempt already written");
            }
            else
            {
                string backup = Task.Run(() => database.BackupAsync(BackupLabel)).GetAwaiter().GetResult();
                store.RecordAttemptBlocking();
                Log(logPath, time, "backup written: " + Path.GetFileName(backup));
            }

            GoalLists existing = store.ReadUnmarkedBlocking() ?? GoalLists.Empty;
            GoalLists seeded = Seed(existing, time.GetUtcNow());
            Log(logPath, time, $"read: areas={existing.Areas.Count} goals={existing.Goals.Count} entries={existing.Entries.Count} "
                + $"reminders={existing.Reminders.Count} reviews={existing.Reviews.Count}; made: areas={seeded.Areas.Count} goals={seeded.Goals.Count}");
            if (!IsBalanced(existing, seeded))
            {
                Log(logPath, time, "counts don't balance; goals not made (no marker written)");
                return GoalModelState.NotMigrated;
            }

            store.ActivateBlocking(seeded);
            Log(logPath, time, $"goals made: {store.Lists.LiveAreas.Count()} live areas, {store.Lists.LiveGoals.Count()} live goals");
            return GoalModelState.Ready;
        }
        catch (Exception ex)
        {
            // Never the message: it could quote a stored value. The type is enough to find it.
            if (migrated)
            {
                Log(logPath, time, "goals could not be read (" + ex.GetType().Name + "); goals closed");
                return GoalModelState.Unreadable;
            }

            Log(logPath, time, "first run failed (" + ex.GetType().Name + "); goals not made (no marker written)");
            return GoalModelState.NotMigrated;
        }
    }

    private static bool Unique(IEnumerable<Guid> ids)
    {
        var seen = new HashSet<Guid>();
        return ids.All(seen.Add);
    }

    private static void Log(string path, TimeProvider time, string line)
    {
        try
        {
            File.AppendAllText(path, time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture) + "  " + line + Environment.NewLine);
        }
        catch (IOException)
        {
            // The log is a help, never a reason to stop.
        }
        catch (UnauthorizedAccessException)
        {
        }

        System.Diagnostics.Debug.WriteLine("goals-model: " + line);
    }
}
