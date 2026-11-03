using System.Runtime.CompilerServices;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The day plan the guards read (ADR 58): the stored plan with classified calendar events laid over it, in memory only
/// (<see cref="CalendarDayOverlay"/>). Plan editors, sync, backup and the plan hash never use it; they keep the stored plan, so
/// a saved plan never holds an event part. With nothing classified and no rule it returns the stored plan itself, so
/// behaviour is exactly as before. Events of every calendar count, visible or not.
/// <para>
/// The result is cached per stored plan until the calendar's events, the classifications or rules or the session state change,
/// or, when the plan has a Budget day (whose credit grows with the clock), the minute: <see cref="DayWorkWindows"/> keeps its
/// own view per plan instance, so the same instance must come back while nothing changed. Also keeps the first-known stamps up
/// to date (on every calendar change, at start-up and when it works out a new result) and promotes a waiting rule set or
/// classification change once its time has come and no session is running.
/// </para>
/// <para>
/// A session counts as running (<see cref="SessionState.IsActive"/>, breaks included, as the looser-change rule has it) until
/// <see cref="MarkSessionStateKnown"/> says the saved session was put back, so nothing loosens, promotes or saves before then.
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
    private volatile bool _sessionStateKnown;

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

    /// <summary>The stretches of counted focus, for leaving them out of a Work event's Budget credit. Set by the app.</summary>
    public Func<IReadOnlyList<LocalWindow>>? CountedFocus { get; set; }

    /// <summary>The effective plan of the plan the app holds now.</summary>
    public DayPlan Current => Of(_configuration.Current.DayPlan);

    /// <summary>A session the user started is running, or the saved session isn't known to be put back yet (fail strict).</summary>
    public bool SessionRuns => !_sessionStateKnown || _sessions.State.IsActive;

    /// <summary>Stamps the events already known at start-up.</summary>
    public void Start()
    {
        Restamp();
    }

    /// <summary>Says that the saved session (if any) has been put back, so <see cref="SessionRuns"/> follows the real state from now on.</summary>
    public void MarkSessionStateKnown()
    {
        lock (_gate)
        {
            _sessionStateKnown = true;
            Interlocked.Increment(ref _calendarVersion);
        }
    }

    /// <summary>The effective plan of <paramref name="stored"/> (the plan the app holds, or one being edited or proposed).</summary>
    public DayPlan Of(DayPlan stored)
    {
        if (Store.Classifications.Count == 0 && Store.PendingClassifications.Count == 0 && Store.Rules.Active.IsEmpty && Store.Rules.Pending is null)
        {
            return stored;
        }

        lock (_gate)
        {
            DateTimeOffset now = _time.GetUtcNow();
            bool sessionRuns = SessionRuns;
            Store.PromoteRulesIfDue(now, sessionRuns);
            Store.PromoteClassificationsIfDue(now, sessionRuns);

            TimeSpan delay = _configuration.Current.Locks.LooseningDelay;
            long minute = HasBudgetDay(stored) ? now.UtcTicks / TimeSpan.TicksPerMinute : 0;
            var key = new CacheKey(Volatile.Read(ref _calendarVersion), Store.Version, minute, sessionRuns, delay);
            if (_cache.TryGetValue(stored, out Cached? cached) && cached.Key == key)
            {
                return cached.Plan;
            }

            Stamp(now);
            DayPlan plan = CalendarDayOverlay.Apply(stored, new CalendarOverlayInput(
                _calendars.AllEvents(),
                Store.Classifications,
                Store.Rules.Active,
                Store.FirstKnown,
                now,
                _time.LocalTimeZone,
                sessionRuns,
                delay,
                NightFallback,
                CountedFocus?.Invoke()));
            _cache.AddOrUpdate(stored, new Cached(key, plan));
            return plan;
        }
    }

    /// <summary>
    /// Schedules a changed rule set to take over after the looser-change delay (changing a rule is a looser change, ADR 58).
    /// </summary>
    /// <returns>False, and nothing is saved, while a session is running.</returns>
    public bool TrySaveRules(CalendarRuleSet set)
    {
        if (SessionRuns)
        {
            return false;
        }

        Store.SchedulePendingRules(set, _time.GetUtcNow() + _configuration.Current.Locks.LooseningDelay);
        return true;
    }

    /// <summary>
    /// Chooses a category for an event or its series (null = "Don't classify"). A change to the same or a stricter category
    /// applies at once; a looser one (including Don't classify) keeps the old choice until the looser-change delay has passed,
    /// by the trusted clock and never during a session (ADR 58).
    /// </summary>
    /// <returns>False, and nothing is saved, while a session is running.</returns>
    public bool TrySetClassification(CalendarEvent calendarEvent, ClassificationScope scope, Guid? categoryId)
    {
        return TryChangeClassification(CalendarOverlayStore.TargetFor(calendarEvent, scope, categoryId), calendarEvent, removes: false);
    }

    /// <summary>Takes a choice back so the rules or the plan decide again; waits like a change to a looser category when it loosens.</summary>
    /// <returns>False, and nothing is saved, while a session is running.</returns>
    public bool TryRemoveClassification(CalendarEvent calendarEvent, ClassificationScope scope)
    {
        return TryChangeClassification(CalendarOverlayStore.TargetFor(calendarEvent, scope, null), calendarEvent, removes: true);
    }

    private bool TryChangeClassification(EventClassification target, CalendarEvent calendarEvent, bool removes)
    {
        lock (_gate)
        {
            if (SessionRuns)
            {
                return false;
            }

            DayPlan plan = _configuration.Current.DayPlan;
            IReadOnlyList<EventClassification> after = removes
                ? CalendarOverlayStore.Without(Store.Classifications, target)
                : CalendarOverlayStore.With(Store.Classifications, target);
            // A whole-series choice covers every kept occurrence of the series, not just the clicked one.
            IEnumerable<CalendarEvent> covered = target.Scope == ClassificationScope.WholeSeries
                ? _calendars.EventsOf(target.CalendarId).Where(candidate => candidate.SeriesId == target.Key).Append(calendarEvent)
                : [calendarEvent];
            if (ClassificationGate.Loosens(covered, plan, Store.Classifications, after, Store.Rules.Active))
            {
                Store.SchedulePendingClassification(target, removes, _time.GetUtcNow() + _configuration.Current.Locks.LooseningDelay);
            }
            else
            {
                Store.ApplyClassificationNow(target, removes);
            }

            return true;
        }
    }

    // The calendar also reports a status check every 30 seconds; only a change of the kept events matters here. Fetches and edits
    // replace a calendar's list of events, so a list that is the same instance as before holds the same events.
    private void OnCalendarChanged()
    {
        IReadOnlyList<IReadOnlyList<CalendarEvent>> lists = _calendars.AllEventLists();
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

        var classified = EventCategoryResolver.Classified(_calendars.AllEvents(), _configuration.Current.DayPlan, Store.Classifications, Store.Rules.Active);
        DateTime localNow = TimeZoneInfo.ConvertTime(now, _time.LocalTimeZone).DateTime;
        Store.SetFirstKnown(FirstKnownTimes.Update(Store.FirstKnown, classified, now, localNow));
    }

    private sealed record CacheKey(int CalendarVersion, int StoreVersion, long Minute, bool SessionRuns, TimeSpan Delay);

    private sealed record Cached(CacheKey Key, DayPlan Plan);
}
