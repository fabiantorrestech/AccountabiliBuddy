using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// Which focus counts toward which goal (plan §3b 5, binding), from what the session data already carries and nothing new:
/// <list type="bullet">
/// <item><b>What counts as focus</b> is the guards' counted focus (<see cref="FocusDayBuilder"/>'s rule and
/// <see cref="DeepWork"/>'s windows: no omitted sessions, no old scheduled blocks, no test sessions (they leave no events), no
/// breaks, interruptions, side tasks, planning, time marked distracted, the PC off or the paused timer) plus the sessions added
/// by hand in History (ADR 20), which count toward goals and never toward a guard. Goal counts never feed a guard, the goal
/// unlock or <c>focusDay</c>.</item>
/// <item><b>The rule:</b> a session has no record of which task was current minute by minute, so <b>all of a session's counted
/// focus counts toward a label goal when any task of its checklist carries that label</b> (the task's label is the checklist
/// task's category: its id, or <see cref="TaskLabel.IdFor"/> its name), and toward a #tag goal when any task carries that tag
/// (the checklist task's ADR 72 labels). A session with no checklist counts only toward "all focus" (Deep work).</item>
/// <item><b>Pieces:</b> a synced session travels in pieces (time away cut out) and only the first carries the checklist
/// (<see cref="SyncFileBuilder"/>). On a receiver a piece with no checklist continues the session before it, from the same
/// device, when neither was added by hand, they have the same profile name, it starts at or after that piece's end, and either
/// the PC was off just before it (<see cref="SyncSession.OffMinutesBefore"/>) or it starts within
/// <see cref="ContinuationGap"/> of it. This PC's own sessions are whole records, so they need no joining.</item>
/// </list>
/// A minute counts once per goal even when several sessions or rules match it (<see cref="FocusWindows"/> unions them), and
/// can count toward several goals.
/// </summary>
public static class GoalFocusAttribution
{
    /// <summary>
    /// The longest gap between two synced pieces still read as one session stepped away from (the PC's longest break is 30
    /// minutes; an hour also covers an interruption).
    /// </summary>
    public static readonly TimeSpan ContinuationGap = TimeSpan.FromMinutes(60);

    /// <summary>
    /// This PC's own sessions: every counted record (the same rule as the day bar, History, the sync file and the guards)
    /// with its checklist, hand-added ones included.
    /// </summary>
    /// <param name="records">Newest first (<see cref="SessionHistoryBuilder.Build"/>).</param>
    /// <param name="tasksOf">A session's checklist by its start (ADR 24 task history), or null for none.</param>
    public static IReadOnlyList<GoalFocusSession> FromRecords(
        IReadOnlyList<SessionRecord> records,
        Func<DateTimeOffset, IReadOnlyList<SessionTask>?> tasksOf,
        Func<DateTimeOffset, bool> isOmitted,
        bool sessionRunning,
        DateTimeOffset now,
        TimeZoneInfo zone)
    {
        return FocusDayBuilder.CountedRecords(records, isOmitted, sessionRunning, now)
            .OrderBy(counted => counted.Record.StartedAt)
            .Select(counted => new GoalFocusSession(
                DeepWork.SessionWindows(counted.Record, counted.End, zone),
                tasksOf(counted.Record.StartedAt) ?? [],
                counted.Record.Mode == SessionMode.Added))
            .ToList();
    }

    /// <summary>
    /// One other device's synced sessions (ADR 22), its pieces joined into sessions (see the class summary). Call it once per
    /// device: pieces from different devices are never joined.
    /// </summary>
    /// <param name="deviceSessions">The sessions of one device's file, any order; one without an end runs until <paramref name="now"/>.</param>
    public static IReadOnlyList<GoalFocusSession> FromSynced(IEnumerable<SyncSession> deviceSessions, DateTimeOffset now, TimeZoneInfo zone)
    {
        var sessions = new List<GoalFocusSession>();
        List<LocalWindow>? windows = null;
        IReadOnlyList<SessionTask> tasks = [];
        bool added = false;
        string? profile = null;
        DateTimeOffset? lastEnd = null;

        void Flush()
        {
            if (windows is not null)
            {
                sessions.Add(new GoalFocusSession(windows, tasks, added));
            }
        }

        foreach (SyncSession piece in deviceSessions.OrderBy(session => session.Start))
        {
            if (!Continues(piece, windows is not null, added, profile, lastEnd))
            {
                Flush();
                windows = [];
                tasks = piece.Tasks ?? [];
                added = piece.Added;
                profile = piece.Profile;
            }

            DateTimeOffset end = piece.End ?? now;
            if (end > piece.Start)
            {
                windows!.Add(new LocalWindow(FocusDayBuilder.Local(piece.Start, zone), FocusDayBuilder.Local(end, zone)));
            }

            // An open piece (the running session) is the last of its session: nothing continues it.
            lastEnd = piece.End;
        }

        Flush();
        return sessions;
    }

    /// <summary>Whether <paramref name="rule"/> takes <paramref name="session"/>'s focus (an off rule takes nothing).</summary>
    /// <param name="tagsAvailable">False where #tag rules aren't counted (the phone until ADR 79, plan §3b 5).</param>
    public static bool Matches(GoalFocusSession session, GoalSourceRule rule, bool tagsAvailable)
    {
        if (!rule.Automatic)
        {
            return false;
        }

        return rule.Type switch
        {
            GoalSourceType.FocusAll => true,
            GoalSourceType.FocusLabel => rule.LabelId is Guid labelId && session.Tasks.Any(task => CarriesLabel(task, labelId)),
            GoalSourceType.FocusTag => tagsAvailable && rule.Tag is string tag && session.Tasks.Any(task => (task.Labels ?? []).Any(label => TaskLabels.Same(label, tag))),
            _ => false,
        };
    }

    /// <summary>
    /// The focus that counts toward a goal: the windows of every session any of its focus rules takes, each minute once,
    /// earliest first.
    /// </summary>
    public static IReadOnlyList<LocalWindow> FocusWindows(IEnumerable<GoalFocusSession> sessions, IReadOnlyList<GoalSourceRule> rules, bool tagsAvailable)
    {
        List<GoalSourceRule> focusRules = rules
            .Where(rule => rule.Type is GoalSourceType.FocusAll or GoalSourceType.FocusLabel or GoalSourceType.FocusTag)
            .ToList();
        if (focusRules.Count == 0)
        {
            return [];
        }

        return DayTimeline.Union(sessions
            .Where(session => focusRules.Any(rule => Matches(session, rule, tagsAvailable)))
            .SelectMany(session => session.Windows));
    }

    private static bool Continues(SyncSession piece, bool hasOpenSession, bool sessionAdded, string? sessionProfile, DateTimeOffset? lastEnd)
    {
        if (!hasOpenSession || piece.Tasks is not null || piece.Added || sessionAdded)
        {
            return false;
        }

        if (sessionProfile is null || !string.Equals(piece.Profile, sessionProfile, StringComparison.Ordinal))
        {
            return false;
        }

        if (lastEnd is not DateTimeOffset previousEnd || piece.Start < previousEnd)
        {
            return false;
        }

        return piece.OffMinutesBefore is not null || piece.Start - previousEnd <= ContinuationGap;
    }

    private static bool CarriesLabel(SessionTask task, Guid labelId)
    {
        return task.CategoryId == labelId || (task.Category is string name && name.Trim().Length > 0 && TaskLabel.IdFor(name) == labelId);
    }
}
