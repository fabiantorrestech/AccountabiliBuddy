using System.Globalization;
using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Goals;
using AccountabiliBuddy.Core.Tasks;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>
/// The weekly goals review on this PC (round 2 R5.4; brief §4.4, §5.8, §5.10; change note §4 "Weekly review card", §4a): when it is
/// due, what it asks, and what was answered. It comes at Settings › Goals' review day and time (the week's first day at 6:00 PM by
/// default) and covers the week before that day (on the user's week start); it stays until every goal it asks about has an answer.
/// Answers are kept as they are made in that week's <see cref="GoalReviewRecord"/> (so "Stop for now" keeps them), with the snooze.
/// <list type="bullet">
/// <item>Snooze (the This week card): AccountabiliBuddy mode hides the row for Settings › Goals' 1 or 2 days, then it asks again;
/// ProductaBuddy mode keeps it snoozed for that week (the row stays, quietly, so it can still be started; brief §5.10).</item>
/// <item>Never during a focus session (like the tidy-up): the row hides and an open review closes, keeping its answers.</item>
/// <item>With "Run the tidy-up straight after" on, the review leads into <see cref="TaskTidyUpService"/>'s flow when it is done (the
/// Shelf's own tidy-up card is hidden then), and the row stays while that tidy-up is due, so it is always reachable through the
/// review.</item>
/// </list>
/// Suggestions only: nothing here locks, waits or feeds a guard ("Pause this goal" pauses at once and is refused for Deep work;
/// "Adjust the target" goes through Edit goal's rules, so lowering Deep work still waits). Use from the UI thread only.
/// </summary>
public sealed class GoalReviewService : IDisposable
{
    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MinWait = TimeSpan.FromSeconds(1);

    private readonly GoalsService _goals;
    private readonly GoalSourceDataService _data;
    private readonly TimeProvider _time;
    private readonly Func<bool> _sessionRunning;
    private readonly DispatcherTimer _timer = new();

    public GoalReviewService(GoalsService goals, GoalSourceDataService data, TimeProvider time, Func<bool> sessionRunning)
    {
        _goals = goals;
        _data = data;
        _time = time;
        _sessionRunning = sessionRunning;
        _timer.Tick += (_, _) => NotifyChanged();
        _goals.Store.Changed += (_, _) => NotifyChanged();
    }

    /// <summary>The weekly tidy-up (task list in use; set by the app): "Run the tidy-up straight after" leads into it.</summary>
    public TaskTidyUpService? TidyUp { get; set; }

    /// <summary>When the row comes or goes: the due moment, a snooze's end, a session, an answer, a settings change.</summary>
    public event EventHandler? Changed;

    public GoalsService Goals => _goals;

    public GoalSourceDataService Data => _data;

    public DateTimeOffset Now => _time.GetUtcNow();

    public bool SessionRunning => _sessionRunning();

    public bool IsProductaBuddy => _goals.IsProductaBuddy;

    private TimeZoneInfo Zone => _time.LocalTimeZone;

    private GoalPageSettings Settings => _goals.Store.PageSettings;

    public void Dispose()
    {
        _timer.Stop();
    }

    /// <summary>Something the row depends on changed (a session started or ended, the mode, the tidy-up): look again.</summary>
    public void NotifyChanged()
    {
        Schedule();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The latest review moment at or before <paramref name="now"/> (the review day at its time, local).</summary>
    public DateTimeOffset DueAt(DateTimeOffset now)
    {
        DayOfWeek day = Settings.ReviewDay;
        TimeOnly time = Settings.ReviewSettings.Time;
        DateOnly today = DayOf(now);
        for (int back = 0; back <= 7; back++)
        {
            DateOnly candidate = today.AddDays(-back);
            if (candidate.DayOfWeek != day)
            {
                continue;
            }

            DateTimeOffset at = TaskReminderTimes.Local(candidate, time, Zone);
            if (at <= now)
            {
                return at;
            }
        }

        return TaskReminderTimes.Local(today.AddDays(-7), time, Zone);
    }

    /// <summary>The first day of the week a review due at <paramref name="due"/> covers: the week (user's week start) of the day before.</summary>
    public DateOnly WeekOf(DateTimeOffset due)
    {
        return GoalPeriods.WeekStartOf(DayOf(due).AddDays(-1), Settings.WeekStart);
    }

    /// <summary>"Nov 8 – 14", "Oct 29 – Nov 4".</summary>
    public static string WeekText(DateOnly start)
    {
        DateOnly end = start.AddDays(6);
        string from = $"{start.ToString("MMM", CultureInfo.InvariantCulture)} {start.Day}";
        return start.Month == end.Month ? $"{from} – {end.Day}" : $"{from} – {end.ToString("MMM", CultureInfo.InvariantCulture)} {end.Day}";
    }

    /// <summary>The week's review record (answers and snooze); null when none was made yet.</summary>
    public GoalReviewRecord? RecordFor(DateOnly week)
    {
        return _goals.Lists.Reviews.Where(record => record.IsLive && record.Week == week).OrderByDescending(record => record.EditedAt).FirstOrDefault();
    }

    /// <summary>The goals a review asks about: every live goal not paused today (Deep work too), in the list's order (areas, then goals).</summary>
    public IReadOnlyList<Goal> GoalsToReview(DateOnly today)
    {
        GoalLists lists = _goals.Lists;
        Dictionary<Guid, double> areaPlace = lists.Areas.Where(area => area.IsLive).ToDictionary(area => area.Id, area => area.Position);
        Goal? deepWork = _goals.DeepWork;
        return lists.LiveGoals
            .Where(goal => !goal.IsPausedOn(today))
            .OrderBy(goal => areaPlace.TryGetValue(goal.AreaId, out double place) ? place : double.MaxValue)
            .ThenBy(goal => goal.Position)
            .Select(goal => goal.IsDeepWork && deepWork is not null ? deepWork : goal)
            .ToList();
    }

    /// <summary>Every goal asked about has an answer in <paramref name="record"/>.</summary>
    public static bool IsComplete(GoalReviewRecord? record, IReadOnlyList<Goal> goals)
    {
        return goals.All(goal => record?.Decisions.Any(decision => decision.GoalId == goal.Id) == true);
    }

    /// <summary>"Run the tidy-up straight after" is on and the task list's tidy-up exists.</summary>
    public bool TidyUpFollows => Settings.ReviewSettings.TidyUpAfter && TidyUp is not null;

    /// <summary>The tidy-up that follows the review is due now (its week isn't handled yet, untouched tasks, no session).</summary>
    public bool TidyUpDue => TidyUpFollows && TidyUp!.IsDueAfterReview();

    /// <summary>The This week card's review row now.</summary>
    public GoalReviewRow Row()
    {
        DateTimeOffset now = Now;
        GoalReviewSettings settings = Settings.ReviewSettings;
        string note = $"{Settings.ReviewDay}s · about 4 min{(settings.TidyUpAfter ? ", then the tidy-up" : string.Empty)}";
        if (!_goals.Store.IsReady || SessionRunning)
        {
            return new GoalReviewRow(false, false, false, note);
        }

        DateOnly week = WeekOf(DueAt(now));
        GoalReviewRecord? record = RecordFor(week);
        bool reviewOpen = !IsComplete(record, GoalsToReview(DayOf(now)));
        bool tidyUpOpen = TidyUpDue;
        if (!reviewOpen && !tidyUpOpen)
        {
            return new GoalReviewRow(false, false, false, note);
        }

        if (record?.SnoozedUntil is DateTimeOffset until && until > now)
        {
            // AB asks again once the snooze ends; PB's snooze stays for the week, the row quiet but still there to start (brief §5.10).
            return IsProductaBuddy
                ? new GoalReviewRow(true, true, false, "Snoozed · start it whenever you like")
                : new GoalReviewRow(false, true, false, note);
        }

        string days = settings.SnoozeDays == 1 ? "1 day" : $"{settings.SnoozeDays} days";
        return new GoalReviewRow(true, false, true, reviewOpen ? note : "The tidy-up is next · about 2 minutes", $"Snooze the weekly review for {days}");
    }

    /// <summary>
    /// The This week card's Snooze: AccountabiliBuddy mode for Settings › Goals' 1 or 2 days (then it asks again); ProductaBuddy mode until
    /// next week's review (it stays snoozed). Answers made so far stay.
    /// </summary>
    public async Task<GoalChangeResult> SnoozeAsync()
    {
        DateTimeOffset now = Now;
        DateTimeOffset due = DueAt(now);
        DateOnly week = WeekOf(due);
        DateTimeOffset until = IsProductaBuddy ? due.AddDays(7) : now.AddDays(Settings.ReviewSettings.SnoozeDays);
        GoalReviewRecord record = RecordFor(week) ?? NewRecord(week);
        GoalChangeResult result = await _goals.SaveReviewAsync(record with { SnoozedUntil = until });
        NotifyChanged();
        return result;
    }

    /// <summary>
    /// One answer, kept at once in the week's record (a later answer for the same goal replaces it). "Pause this goal" pauses it from
    /// today first and is refused for Deep work (nothing is recorded then); the others change nothing by themselves.
    /// </summary>
    public async Task<GoalChangeResult> DecideAsync(DateOnly week, Guid goalId, GoalReviewChoice choice)
    {
        if (SessionRunning)
        {
            return GoalChangeResult.NotAllowed;
        }

        if (choice == GoalReviewChoice.Pause)
        {
            GoalChangeResult paused = await _goals.PauseGoalAsync(goalId);
            if (paused is not (GoalChangeResult.Applied or GoalChangeResult.Unchanged))
            {
                return paused;
            }
        }

        GoalReviewRecord record = RecordFor(week) ?? NewRecord(week);
        List<GoalReviewDecision> decisions = record.Decisions.Where(decision => decision.GoalId != goalId).ToList();
        decisions.Add(new GoalReviewDecision(goalId, choice));
        return await _goals.SaveReviewAsync(record with { Decisions = decisions });
    }

    /// <summary>The review's suggestion and the facts it used (brief §5.8), for a goal as of today.</summary>
    public GoalReviewSuggestion SuggestionFor(Goal goal)
    {
        return GoalReviewSuggestion.For(goal, _data.Current, _data.Today, Settings.WeekStart);
    }

    private GoalReviewRecord NewRecord(DateOnly week)
    {
        double position = _goals.Lists.Reviews.Select(record => record.Position).DefaultIfEmpty(0).Max() + 1;
        return new GoalReviewRecord(Guid.NewGuid(), week, [], position, Now);
    }

    // Wakes at the next due moment or snooze end (at most every 30 minutes), so the row comes without a restart.
    private void Schedule()
    {
        DateTimeOffset now = Now;
        DateTimeOffset next = DueAt(now).AddDays(7);
        if (_goals.Store.IsReady && RecordFor(WeekOf(DueAt(now)))?.SnoozedUntil is DateTimeOffset until && until > now && until < next)
        {
            next = until;
        }

        TimeSpan wait = TimeSpan.FromTicks(Math.Clamp((next - now).Ticks, MinWait.Ticks, MaxWait.Ticks));
        _timer.Stop();
        _timer.Interval = wait;
        _timer.Start();
    }

    private DateOnly DayOf(DateTimeOffset at)
    {
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, Zone).DateTime);
    }
}
