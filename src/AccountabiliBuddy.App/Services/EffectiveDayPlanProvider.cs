using System.Runtime.CompilerServices;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The day plan the guards read (ADR 58): the stored plan with classified calendar events laid over it, in memory only
/// (<see cref="CalendarDayOverlay"/>). Plan editors, sync, backup and the plan hash never use it; they keep the stored plan, so
/// a saved plan never holds an event part. With nothing classified and no rule it returns the stored plan itself, so
/// behaviour is exactly as before.
/// <para>
/// The result is cached per stored plan until the calendar's events, the classifications or rules or the session state change,
/// or, when the plan has a Budget day (whose credit grows with the clock), the minute: <see cref="DayWorkWindows"/> keeps its
/// own view per plan instance, so the same instance must come back while nothing changed. Also keeps the first-known stamps up to date (on every calendar change, at start-up and when it works
/// out a new result) and promotes a waiting rule set once its time has come and no session the user started is running.
/// </para>
/// </summary>
public sealed class EffectiveDayPlanProvider
{
    private readonly ConfigurationService _configuration;
    private readonly CalendarService _calendars;
    private readonly SessionController _sessions;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly ConditionalWeakTable<DayPlan, Cached> _cache = new();
    private int _calendarVersion;
    private IReadOnlyList<IReadOnlyList<CalendarEvent>> _seenEventLists = Array.Empty<IReadOnlyList<CalendarEvent>>();

    public EffectiveDayPlanProvider(ConfigurationService configuration, CalendarService calendars, CalendarOverlayStore store, SessionController sessions, TimeProvider time)
    {
        _configuration = configuration;
        _calendars = calendars;
        Store = store;
        _sessions = sessions;
        _time = time;
        _calendars.Changed += (_, _) => OnCalendarChanged();
        Store.Changed += (_, _) => Restamp();
    }

    /// <summary>The classifications, rules and first-known stamps kept on this computer.</summary>
    public CalendarOverlayStore Store { get; }

    /// <summary>The night before a date from the sleep settings, for a Budget day whose plan has no Sleep part. Set by the app.</summary>
    public Func<DateOnly, SleepTimes?>? NightFallback { get; set; }

    /// <summary>The effective plan of the plan the app holds now.</summary>
    public DayPlan Current => Of(_configuration.Current.DayPlan);

    /// <summary>Stamps the events already known at start-up.</summary>
    public void Start()
    {
        Restamp();
    }

    /// <summary>The effective plan of <paramref name="stored"/> (the plan the app holds, or one being edited).</summary>
    public DayPlan Of(DayPlan stored)
    {
        if (Store.Classifications.Count == 0 && Store.Rules.Active.IsEmpty && Store.Rules.Pending is null)
        {
            return stored;
        }

        lock (_gate)
        {
            DateTimeOffset now = _time.GetUtcNow();
            bool sessionRuns = _sessions.IsEnforcing;
            Store.PromoteRulesIfDue(now, sessionRuns);

            TimeSpan delay = _configuration.Current.Locks.LooseningDelay;
            long minute = HasBudgetDay(stored) ? now.UtcTicks / TimeSpan.TicksPerMinute : 0;
            var key = new CacheKey(Volatile.Read(ref _calendarVersion), Store.Version, minute, sessionRuns, delay);
            if (_cache.TryGetValue(stored, out Cached? cached) && cached.Key == key)
            {
                return cached.Plan;
            }

            Stamp(now);
            DayPlan plan = CalendarDayOverlay.Apply(stored, new CalendarOverlayInput(
                _calendars.VisibleEvents(),
                Store.Classifications,
                Store.Rules.Active,
                Store.FirstKnown,
                now,
                _time.LocalTimeZone,
                sessionRuns,
                delay,
                NightFallback));
            _cache.AddOrUpdate(stored, new Cached(key, plan));
            return plan;
        }
    }

    /// <summary>
    /// Schedules a changed rule set to take over after the looser-change delay (changing a rule is a looser change, ADR 58).
    /// </summary>
    /// <returns>False, and nothing is saved, while a session the user started is running.</returns>
    public bool TrySaveRules(CalendarRuleSet set)
    {
        if (_sessions.IsEnforcing)
        {
            return false;
        }

        Store.SchedulePendingRules(set, _time.GetUtcNow() + _configuration.Current.Locks.LooseningDelay);
        return true;
    }

    // The calendar also reports a status check every 30 seconds; only a change of the kept events matters here. Fetches and edits
    // replace a calendar's list of events, so a list that is the same instance as before holds the same events.
    private void OnCalendarChanged()
    {
        IReadOnlyList<IReadOnlyList<CalendarEvent>> lists = _calendars.VisibleEventLists();
        lock (_gate)
        {
            if (lists.Count == _seenEventLists.Count && lists.Zip(_seenEventLists).All(pair => ReferenceEquals(pair.First, pair.Second)))
            {
                return;
            }

            _seenEventLists = lists;
            Interlocked.Increment(ref _calendarVersion);
            Stamp(_time.GetUtcNow());
        }
    }

    private static bool HasBudgetDay(DayPlan plan)
    {
        return plan.Templates.Any(template => template.Template.Mode == DayMode.Budget) || plan.OneOffDays.Any(day => day.Template.Mode == DayMode.Budget);
    }

    private void Restamp()
    {
        lock (_gate)
        {
            Stamp(_time.GetUtcNow());
        }
    }

    // First sight of an event at its current start, end and category, by the trusted clock; ended and unclassified ones are forgotten.
    private void Stamp(DateTimeOffset now)
    {
        if (Store.FirstKnown.Count == 0 && Store.Classifications.Count == 0 && Store.Rules.Active.IsEmpty)
        {
            return;
        }

        var classified = EventCategoryResolver.Classified(_calendars.VisibleEvents(), _configuration.Current.DayPlan, Store.Classifications, Store.Rules.Active);
        DateTime localNow = TimeZoneInfo.ConvertTime(now, _time.LocalTimeZone).DateTime;
        Store.SetFirstKnown(FirstKnownTimes.Update(Store.FirstKnown, classified, now, localNow));
    }

    private sealed record CacheKey(int CalendarVersion, int StoreVersion, long Minute, bool SessionRuns, TimeSpan Delay);

    private sealed record Cached(CacheKey Key, DayPlan Plan);
}
