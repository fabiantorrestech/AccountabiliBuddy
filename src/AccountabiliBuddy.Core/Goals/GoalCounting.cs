using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// A goal's day values and day statuses (brief §5.4; change note §4, §4a), computed from its sources, never stored:
/// <list type="bullet">
/// <item><b>Time</b> sources (focus rules through <see cref="GoalFocusAttribution"/>, calendar categories, and time added by
/// hand with a start) are joined as windows, so a minute counts once per goal however many rules match it and overlapping
/// sources count once. Hand time without a start, and each reading source's latest reading of the day (screen time's derived
/// day value), add on top. Whole minutes: the windows' total is rounded down, as the Deep work card does.</item>
/// <item><b>Amount</b> goals: the day's value is the latest hand edit if there is one (it wins over any later reading), else
/// the day's first reading.</item>
/// <item><b>Habits</b>: the latest hand entry decides (a tick, or an explicit untick that wins over the threshold); without one
/// the day ticks itself once its time sources reach the smallest <see cref="GoalSourceRule.ThresholdMinutes"/>.</item>
/// <item><b>Milestones</b>: the day's hand entries added up (their task lists are "Coming soon").</item>
/// <item>A paused day is neither hit nor missed; a day before the goal existed or after today asks for nothing.</item>
/// <item>Hand time counts toward goals, never toward a guard: guards, the goal unlock and <c>focusDay</c> never read these values,
/// so back-filling a past day updates its week and streak and never changes a guard decision already made.</item>
/// </list>
/// Readings only count from a source the goal has a switched-on rule for; hand entries always count (the user's own decision).
/// </summary>
public static class GoalCounting
{
    /// <summary>The goal's days from <paramref name="from"/> to <paramref name="to"/>, both included.</summary>
    public static IReadOnlyList<GoalDayState> Days(Goal goal, GoalSourceData data, DateOnly from, DateOnly to, DateOnly today)
    {
        var context = new Context(goal, data);
        var days = new List<GoalDayState>();
        for (DateOnly day = from; day <= to; day = day.AddDays(1))
        {
            days.Add(context.Day(day, today));
        }

        return days;
    }

    /// <summary>One day of the goal.</summary>
    public static GoalDayState Day(Goal goal, GoalSourceData data, DateOnly day, DateOnly today)
    {
        return new Context(goal, data).Day(day, today);
    }

    /// <summary>
    /// The goal's days from the day it was created (or <paramref name="from"/>, when later) to <paramref name="today"/>, by day.
    /// </summary>
    public static IReadOnlyDictionary<DateOnly, GoalDayState> History(Goal goal, GoalSourceData data, DateOnly today, DateOnly? from = null)
    {
        DateOnly created = goal.CreatedOn(data.Zone);
        DateOnly start = from is DateOnly earliest && earliest > created ? earliest : created;
        return start > today ? new Dictionary<DateOnly, GoalDayState>()
            : Days(goal, data, start, today, today).ToDictionary(state => state.Day);
    }

    /// <summary>
    /// Whether days can be hit one by one: any habit, or a daily target or "N of the period's days" with an amount. A goal with
    /// no period amount (Deep work's "Track time only", a long-term target only) has no day to hit or miss.
    /// </summary>
    public static bool CountsDays(Goal goal)
    {
        return goal.Kind == GoalKind.Habit || (goal.Target.Amount is not null && goal.Target.CountsDays);
    }

    /// <summary>
    /// For a <see cref="GoalDirection.Reach"/> target: whether the level lies below the goal's first value (a weight to lose).
    /// False with no value yet.
    /// </summary>
    public static bool ReachesDown(Goal goal, GoalSourceData data)
    {
        double? level = goal.Target.TargetAmount ?? goal.Target.Amount;
        double? first = FirstValue(goal, data);
        return level is double target && first is double start && target < start;
    }

    /// <summary>The goal's first day value ever (the earliest day with a live entry, by the Amount rule); null with none.</summary>
    public static double? FirstValue(Goal goal, GoalSourceData data)
    {
        HashSet<GoalEntrySource> readable = ReadableSources(goal);
        IGrouping<DateOnly, GoalEntry>? first = data.Entries
            .Where(entry => entry.GoalId == goal.Id && entry.IsLive && (entry.Source == GoalEntrySource.Hand || readable.Contains(entry.Source)))
            .GroupBy(entry => entry.Day)
            .OrderBy(group => group.Key)
            .FirstOrDefault();
        return first is null ? null : AmountValue(first.ToList(), readable).Value;
    }

    /// <summary>Which entry source a reading rule reads; null for a rule that counts time or ticks.</summary>
    public static GoalEntrySource? EntrySourceOf(GoalSourceType type)
    {
        return type switch
        {
            GoalSourceType.HealthSteps or GoalSourceType.HealthWeight or GoalSourceType.HealthSleep => GoalEntrySource.HealthConnect,
            GoalSourceType.ScreenTime => GoalEntrySource.ScreenTime,
            GoalSourceType.Beeminder => GoalEntrySource.Beeminder,
            _ => null,
        };
    }

    private static HashSet<GoalEntrySource> ReadableSources(Goal goal)
    {
        return goal.Sources
            .Where(rule => rule.Automatic)
            .Select(rule => EntrySourceOf(rule.Type))
            .OfType<GoalEntrySource>()
            .ToHashSet();
    }

    // The latest hand entry first (its edit time, then its id, so every device picks the same one).
    private static GoalEntry? LatestHand(IEnumerable<GoalEntry> entries)
    {
        return entries
            .Where(entry => entry.Source == GoalEntrySource.Hand)
            .OrderByDescending(entry => entry.EditedAt)
            .ThenByDescending(entry => entry.Id.ToString("D"), StringComparer.Ordinal)
            .FirstOrDefault();
    }

    // An Amount day: the latest hand edit, else the first reading (by its time, then edit time, then id).
    private static (double? Value, bool ByHand) AmountValue(IReadOnlyList<GoalEntry> dayEntries, HashSet<GoalEntrySource> readable)
    {
        if (LatestHand(dayEntries) is GoalEntry hand)
        {
            return (hand.Value, true);
        }

        GoalEntry? first = dayEntries
            .Where(entry => entry.Source != GoalEntrySource.Hand && readable.Contains(entry.Source))
            .OrderBy(entry => entry.Time ?? TimeOnly.MinValue)
            .ThenBy(entry => entry.EditedAt)
            .ThenBy(entry => entry.Id.ToString("D"), StringComparer.Ordinal)
            .FirstOrDefault();
        return (first?.Value, false);
    }

    private static double WholeMinutes(IEnumerable<LocalWindow> windows)
    {
        double minutes = DayTimeline.Union(windows).Sum(window => (window.End - window.Start).TotalMinutes);
        return Math.Floor(minutes + 1e-9);
    }

    /// <summary>The goal's sources prepared once, then read day by day.</summary>
    private sealed class Context
    {
        private readonly Goal _goal;
        private readonly GoalSourceData _data;
        private readonly DateOnly _createdOn;
        private readonly List<(GoalSourceRule Rule, IReadOnlyList<LocalWindow> Windows)> _timeRules;
        private readonly HashSet<GoalEntrySource> _readable;
        private readonly Dictionary<DateOnly, List<GoalEntry>> _entriesByDay;
        private readonly double? _threshold;
        private readonly bool _reachesDown;

        public Context(Goal goal, GoalSourceData data)
        {
            _goal = goal;
            _data = data;
            _createdOn = goal.CreatedOn(data.Zone);
            _timeRules = goal.Sources
                .Where(rule => rule.Automatic && rule.CountsTime)
                .Select(rule => (rule, WindowsOf(rule, data)))
                .ToList();
            _readable = ReadableSources(goal);
            _entriesByDay = data.Entries
                .Where(entry => entry.GoalId == goal.Id && entry.IsLive)
                .GroupBy(entry => entry.Day)
                .ToDictionary(group => group.Key, group => group.ToList());
            _threshold = goal.Sources
                .Where(rule => rule.Automatic && rule.CountsTime && rule.ThresholdMinutes is > 0)
                .Select(rule => rule.ThresholdMinutes)
                .Min();
            _reachesDown = goal.Target.Direction == GoalDirection.Reach && ReachesDown(goal, data);
        }

        public GoalDayState Day(DateOnly day, DateOnly today)
        {
            List<GoalEntry> dayEntries = _entriesByDay.TryGetValue(day, out List<GoalEntry>? found) ? found : [];
            var sources = new List<GoalSourceType>();
            DateTime dayStart = day.ToDateTime(TimeOnly.MinValue);
            var dayWindow = new LocalWindow(dayStart, dayStart.AddDays(1));

            // Time sources, each minute once.
            var autoWindows = new List<LocalWindow>();
            foreach ((GoalSourceRule rule, IReadOnlyList<LocalWindow> windows) in _timeRules)
            {
                List<LocalWindow> clipped = windows.Select(window => DayTimeline.Clip(window, dayWindow)).OfType<LocalWindow>().ToList();
                if (clipped.Count > 0 && !sources.Contains(rule.Type))
                {
                    sources.Add(rule.Type);
                }

                autoWindows.AddRange(clipped);
            }

            double autoMinutes = WholeMinutes(autoWindows);

            // Readings from the goal's switched-on reading sources.
            List<GoalEntry> readings = dayEntries.Where(entry => entry.Source != GoalEntrySource.Hand && _readable.Contains(entry.Source)).ToList();
            foreach (GoalSourceRule rule in _goal.Sources.Where(rule => rule.Automatic && EntrySourceOf(rule.Type) is GoalEntrySource source && readings.Any(entry => entry.Source == source)))
            {
                if (!sources.Contains(rule.Type))
                {
                    sources.Add(rule.Type);
                }
            }

            List<GoalEntry> hand = dayEntries.Where(entry => entry.Source == GoalEntrySource.Hand).ToList();
            bool backFilled = hand.Any(entry => entry.IsBackFill(_data.Zone));

            double value;
            double minutes = autoMinutes;
            double valueWithoutHand;
            bool hasValue;
            bool byHand;
            bool? handDecision = null; // A habit's tick (true) or explicit untick (false) by hand.
            switch (_goal.Kind)
            {
                case GoalKind.Time:
                {
                    List<GoalEntry> handTime = hand.Where(entry => entry.Value > 0).ToList();
                    IEnumerable<LocalWindow> timed = handTime
                        .Where(entry => entry.Time is not null)
                        .Select(entry =>
                        {
                            DateTime start = day.ToDateTime(entry.Time!.Value);
                            return DayTimeline.Clip(new LocalWindow(start, start.AddMinutes(entry.Value)), dayWindow);
                        })
                        .OfType<LocalWindow>();
                    double untimed = handTime.Where(entry => entry.Time is null).Sum(entry => entry.Value);
                    double readingTotal = readings
                        .GroupBy(entry => entry.Source)
                        .Sum(group => group.OrderByDescending(entry => entry.EditedAt).ThenByDescending(entry => entry.Id.ToString("D"), StringComparer.Ordinal).First().Value);
                    minutes = WholeMinutes(autoWindows.Concat(timed)) + untimed;
                    value = minutes + readingTotal;
                    valueWithoutHand = autoMinutes + readingTotal;
                    byHand = handTime.Count > 0;
                    hasValue = value > 0;
                    break;
                }

                case GoalKind.Habit:
                {
                    GoalEntry? latest = LatestHand(hand);
                    handDecision = latest is null ? null : latest.Value > 0;
                    bool autoHit = _threshold is double threshold && autoMinutes >= threshold;
                    bool ticked = handDecision ?? autoHit;
                    value = ticked ? 1 : 0;
                    valueWithoutHand = autoHit ? 1 : 0;
                    byHand = latest is not null;
                    hasValue = ticked || autoMinutes > 0 || latest is not null;
                    break;
                }

                case GoalKind.Amount:
                {
                    (double? amount, bool fromHand) = AmountValue(dayEntries, _readable);
                    value = amount ?? 0;
                    valueWithoutHand = value;
                    byHand = fromHand;
                    hasValue = amount is not null;
                    break;
                }

                default:
                {
                    value = hand.Sum(entry => entry.Value);
                    valueWithoutHand = 0;
                    byHand = hand.Count > 0;
                    hasValue = value > 0;
                    break;
                }
            }

            if (byHand)
            {
                sources.Add(GoalSourceType.Hand);
            }

            bool countsDays = CountsDays(_goal);
            bool hit = countsDays && IsHit(value, hasValue, handDecision);
            bool hitWithoutHand = _goal.Kind == GoalKind.Habit ? handDecision is null && hit
                : _goal.Kind == GoalKind.Time ? countsDays && IsHit(valueWithoutHand, valueWithoutHand > 0, null)
                : hit && !byHand;

            if (day > today || day < _createdOn)
            {
                return State(day, value, minutes, hasValue, false, GoalDayStatus.NotDue, 0, sources, byHand, backFilled);
            }

            if (_goal.IsPausedOn(day))
            {
                return State(day, value, minutes, hasValue, false, GoalDayStatus.Paused, 0, sources, byHand, backFilled);
            }

            if (!countsDays)
            {
                GoalDayStatus loose = day == today ? GoalDayStatus.InProgress
                    : hasValue ? (byHand ? GoalDayStatus.Counted : GoalDayStatus.CountedTickedItself)
                    : GoalDayStatus.NotDue;
                return State(day, value, minutes, hasValue, false, loose, 0, sources, byHand, backFilled);
            }

            if (hit && (day < today || _goal.Target.Direction != GoalDirection.AtMost))
            {
                GoalDayStatus counted = hitWithoutHand ? GoalDayStatus.CountedTickedItself : GoalDayStatus.Counted;
                return State(day, value, minutes, hasValue, true, counted, 0, sources, byHand, backFilled);
            }

            double gap = Gap(value, minutes);
            bool over = _goal.Target.Direction == GoalDirection.AtMost && !hit;
            if (day == today)
            {
                // An at-most day under its cap isn't hit until it is over.
                GoalDayStatus todayStatus = over ? GoalDayStatus.NotCounted : GoalDayStatus.InProgress;
                double toGo = over || _goal.Kind == GoalKind.Habit || _goal.Target.Direction == GoalDirection.AtLeast ? gap : 0;
                return State(day, value, minutes, hasValue, false, todayStatus, toGo, sources, byHand, backFilled);
            }

            // An explicit untick by hand is the user's decision: the day reads as missed, whatever the time sources say.
            bool someProgress = _goal.Kind == GoalKind.Habit ? minutes > 0 && handDecision != false : hasValue && value > 0;
            if (someProgress || over)
            {
                return State(day, value, minutes, hasValue, false, GoalDayStatus.NotCounted, gap, sources, byHand, backFilled);
            }

            // Nothing on a day that was asked for: missed on a daily goal; a day an "N of the period's days" goal didn't need is not due.
            GoalDayStatus empty = _goal.Target.Period == GoalPeriod.Day ? GoalDayStatus.Missed : GoalDayStatus.NotDue;
            return State(day, value, minutes, hasValue, false, empty, 0, sources, byHand, backFilled);
        }

        private bool IsHit(double value, bool hasValue, bool? handDecision)
        {
            if (_goal.Kind == GoalKind.Habit)
            {
                return value >= 1 || handDecision == true;
            }

            if (_goal.Target.Amount is not double amount)
            {
                return false;
            }

            return _goal.Target.Direction switch
            {
                GoalDirection.AtMost => value <= amount,
                GoalDirection.Reach => hasValue && (_reachesDown ? value <= amount : value >= amount),
                _ => hasValue && value >= amount,
            };
        }

        // "N short" / "N to go" / "N over": in minutes for a habit with a threshold, else in the goal's unit.
        private double Gap(double value, double minutes)
        {
            if (_goal.Kind == GoalKind.Habit)
            {
                return _threshold is double threshold ? Math.Max(0, threshold - minutes) : 0;
            }

            if (_goal.Target.Amount is not double amount)
            {
                return 0;
            }

            return _goal.Target.Direction switch
            {
                GoalDirection.AtMost => Math.Max(0, value - amount),
                GoalDirection.Reach => Math.Abs(value - amount),
                _ => Math.Max(0, amount - value),
            };
        }

        private static GoalDayState State(
            DateOnly day, double value, double minutes, bool hasValue, bool hit, GoalDayStatus status, double gap,
            List<GoalSourceType> sources, bool byHand, bool backFilled)
        {
            return new GoalDayState(day, value, minutes, hasValue, hit, status, gap, sources, byHand, backFilled);
        }

        private static IReadOnlyList<LocalWindow> WindowsOf(GoalSourceRule rule, GoalSourceData data)
        {
            if (rule.Type == GoalSourceType.CalendarCategory)
            {
                return rule.CalendarCategoryId is Guid category && data.CalendarWindows is { } calendar && calendar.TryGetValue(category, out IReadOnlyList<LocalWindow>? windows)
                    ? DayTimeline.Union(windows)
                    : [];
            }

            return GoalFocusAttribution.FocusWindows(data.Focus, [rule], data.TagsAvailable);
        }
    }
}
