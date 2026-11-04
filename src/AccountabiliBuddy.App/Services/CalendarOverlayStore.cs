using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// What this computer keeps so calendar events can shape the day plan's guarded windows (ADR 58), in the documents table and
/// never in the shared profile JSON:
/// <list type="bullet">
/// <item>"calendar.classifications": the category chosen on an event or its series ("Don't classify" included), and
/// "calendar.classifications.pending": looser changes of them waiting for the delay.</item>
/// <item>"calendar.rules": the per-calendar rules in force and a changed set waiting to take over.</item>
/// <item>"calendar.firstKnown": when each classified event was first seen at its current time and category (the notice
/// a looser change needs). Not backed up or restored: restoring it would hand out notice that was never served.</item>
/// </list>
/// Nothing here is backed up or restored: a restore would skip the waits. Reads are snapshots of immutable lists and may come
/// from any thread; changes are made under a lock and saved one after another so an older state never overwrites a newer one.
/// The gate on classification and rule changes (refused during a session, a looser one waits) lives in
/// <see cref="EffectiveDayPlanProvider"/>; the members here that skip it are <c>internal</c>, which only keeps other assemblies out: any code in this App assembly can still call them, so callers must go through the provider.
/// </summary>
public sealed class CalendarOverlayStore
{
    public const string ClassificationsKey = "calendar.classifications";
    public const string PendingClassificationsKey = "calendar.classifications.pending";
    public const string RulesKey = "calendar.rules";
    public const string FirstKnownKey = "calendar.firstKnown";

    private readonly SqliteJsonDocumentStore _store;
    private readonly object _gate = new();
    private Task _pendingSave = Task.CompletedTask;
    private int _version;

    public CalendarOverlayStore(SqliteJsonDocumentStore store)
    {
        _store = store;
    }

    public IReadOnlyList<EventClassification> Classifications { get; private set; } = Array.Empty<EventClassification>();

    /// <summary>Looser classification changes waiting for their time; the choices in <see cref="Classifications"/> stay in force meanwhile.</summary>
    public IReadOnlyList<PendingClassification> PendingClassifications { get; private set; } = Array.Empty<PendingClassification>();

    public CalendarRules Rules { get; private set; } = CalendarRules.Empty;

    public IReadOnlyList<FirstKnownEntry> FirstKnown { get; private set; } = Array.Empty<FirstKnownEntry>();

    /// <summary>Counts every change of what the overlay reads (classifications, rules); first-known stamps don't count.</summary>
    public int Version => Volatile.Read(ref _version);

    /// <summary>Raised when classifications or rules change (not for first-known stamps).</summary>
    public event EventHandler? Changed;

    public void LoadBlocking()
    {
        try
        {
            Classifications = Load<List<EventClassification>>(ClassificationsKey) ?? (IReadOnlyList<EventClassification>)Array.Empty<EventClassification>();
            PendingClassifications = Load<List<PendingClassification>>(PendingClassificationsKey) ?? (IReadOnlyList<PendingClassification>)Array.Empty<PendingClassification>();
            Rules = Load<CalendarRules>(RulesKey) ?? CalendarRules.Empty;
            FirstKnown = Load<List<FirstKnownEntry>>(FirstKnownKey) ?? (IReadOnlyList<FirstKnownEntry>)Array.Empty<FirstKnownEntry>();
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            // Unreadable data: start with nothing classified, which leaves the plan exactly as stored.
        }
    }

    /// <summary>
    /// The choice for one event or its whole series; a null <paramref name="categoryId"/> is "Don't classify". A series choice on
    /// an event that doesn't repeat is a choice for that event.
    /// </summary>
    public static EventClassification TargetFor(CalendarEvent calendarEvent, ClassificationScope scope, Guid? categoryId)
    {
        bool series = scope == ClassificationScope.WholeSeries && calendarEvent.SeriesId is not null;
        return series
            ? new EventClassification(calendarEvent.CalendarId, calendarEvent.SeriesId!, ClassificationScope.WholeSeries, categoryId)
            : new EventClassification(calendarEvent.CalendarId, calendarEvent.ClassificationKey, ClassificationScope.ThisEvent, categoryId);
    }

    /// <summary>The list with <paramref name="classification"/> stored, replacing a choice for the same target.</summary>
    public static IReadOnlyList<EventClassification> With(IReadOnlyList<EventClassification> list, EventClassification classification)
    {
        return list.Where(existing => !SameTarget(existing, classification)).Append(classification).ToList();
    }

    /// <summary>The list without the choice for <paramref name="target"/>.</summary>
    public static IReadOnlyList<EventClassification> Without(IReadOnlyList<EventClassification> list, EventClassification target)
    {
        return list.Where(existing => !SameTarget(existing, target)).ToList();
    }

    /// <summary>Stores or removes a choice at once and drops any change waiting for the same target.</summary>
    internal void ApplyClassificationNow(EventClassification target, bool removes)
    {
        lock (_gate)
        {
            Classifications = removes ? Without(Classifications, target) : With(Classifications, target);
            PendingClassifications = PendingClassifications.Where(pending => !SameTarget(pending.Change, target)).ToList();
            SaveClassifications();
            SavePendingClassifications();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Schedules a looser change to take over at <paramref name="takesOverAt"/>, replacing one already waiting for the same target.</summary>
    internal void SchedulePendingClassification(EventClassification target, bool removes, DateTimeOffset takesOverAt)
    {
        lock (_gate)
        {
            PendingClassifications = PendingClassifications
                .Where(pending => !SameTarget(pending.Change, target))
                .Append(new PendingClassification(target, removes, takesOverAt))
                .ToList();
            SavePendingClassifications();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Makes every waiting classification change whose time has come take over, unless a session the user started runs.</summary>
    /// <returns>True when any took over.</returns>
    public bool PromoteClassificationsIfDue(DateTimeOffset now, bool sessionRuns)
    {
        lock (_gate)
        {
            if (sessionRuns || !PendingClassifications.Any(pending => pending.TakesOverAt <= now))
            {
                return false;
            }

            foreach (PendingClassification due in PendingClassifications.Where(pending => pending.TakesOverAt <= now).OrderBy(pending => pending.TakesOverAt))
            {
                Classifications = due.Removes ? Without(Classifications, due.Change) : With(Classifications, due.Change);
            }

            PendingClassifications = PendingClassifications.Where(pending => pending.TakesOverAt > now).ToList();
            SaveClassifications();
            SavePendingClassifications();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Schedules <paramref name="set"/> to replace the active rules at <paramref name="takesOverAt"/> (a rule change is a
    /// looser change); a set already waiting is replaced and the wait starts over. The caller decides whether a session
    /// forbids it (<see cref="EffectiveDayPlanProvider.TrySaveRules"/>).
    /// </summary>
    internal void SchedulePendingRules(CalendarRuleSet set, DateTimeOffset takesOverAt)
    {
        lock (_gate)
        {
            Rules = Rules.WithPending(set, takesOverAt);
            SaveRules();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Drops the waiting set, if any, keeping the active rules.</summary>
    public void CancelPendingRules()
    {
        lock (_gate)
        {
            if (Rules.Pending is null)
            {
                return;
            }

            Rules = new CalendarRules(Rules.Active);
            SaveRules();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Makes the waiting set the active one when its time has come and no session the user started runs.</summary>
    /// <returns>True when it took over.</returns>
    public bool PromoteRulesIfDue(DateTimeOffset now, bool sessionRuns)
    {
        lock (_gate)
        {
            if (sessionRuns || !Rules.PendingIsDue(now))
            {
                return false;
            }

            Rules = Rules.Promoted();
            SaveRules();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Forgets stored and waiting choices for <paramref name="targets"/> (choices no guard can read any more, see
    /// <see cref="Core.Engine.ClassificationPruning"/>). Not gated: nothing here can change a guard.
    /// </summary>
    internal void RemoveStale(IReadOnlyCollection<EventClassification> targets)
    {
        if (targets.Count == 0)
        {
            return;
        }

        lock (_gate)
        {
            Classifications = Classifications.Where(existing => !targets.Any(target => SameTarget(existing, target))).ToList();
            PendingClassifications = PendingClassifications.Where(pending => !targets.Any(target => SameTarget(pending.Change, target))).ToList();
            SaveClassifications();
            SavePendingClassifications();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Forgets everything kept for calendars that are gone: their rules (active and waiting), classifications (stored and
    /// waiting) and first-known stamps. The caller decides whether a session forbids it (<see cref="EffectiveDayPlanProvider.TryForgetCalendars"/>).
    /// </summary>
    internal void ForgetCalendars(IReadOnlyCollection<string> calendarIds)
    {
        if (calendarIds.Count == 0)
        {
            return;
        }

        lock (_gate)
        {
            Classifications = Classifications.Where(existing => !calendarIds.Contains(existing.CalendarId)).ToList();
            PendingClassifications = PendingClassifications.Where(pending => !calendarIds.Contains(pending.Change.CalendarId)).ToList();
            Rules = new CalendarRules(WithoutCalendars(Rules.Active, calendarIds), Rules.Pending is null ? null : WithoutCalendars(Rules.Pending, calendarIds), Rules.PendingTakesOverAt);
            FirstKnown = FirstKnown.Where(entry => !calendarIds.Contains(entry.CalendarId)).ToList();
            SaveClassifications();
            SavePendingClassifications();
            SaveRules();
            Save(FirstKnownKey, FirstKnown);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static CalendarRuleSet WithoutCalendars(CalendarRuleSet set, IReadOnlyCollection<string> calendarIds)
    {
        return set with { Rules = set.Rules.Where(rule => !calendarIds.Contains(rule.CalendarId)).ToList() };
    }

    /// <summary>Replaces the first-known stamps when <paramref name="updated"/> is a different list than the one kept.</summary>
    public void SetFirstKnown(IReadOnlyList<FirstKnownEntry> updated)
    {
        lock (_gate)
        {
            if (ReferenceEquals(updated, FirstKnown))
            {
                return;
            }

            FirstKnown = updated;
            Save(FirstKnownKey, FirstKnown);
        }
    }

    private static bool SameTarget(EventClassification first, EventClassification second)
    {
        return first.CalendarId == second.CalendarId && first.Scope == second.Scope && first.Key == second.Key;
    }

    private void SaveClassifications()
    {
        Interlocked.Increment(ref _version);
        Save(ClassificationsKey, Classifications);
    }

    private void SavePendingClassifications()
    {
        Interlocked.Increment(ref _version);
        Save(PendingClassificationsKey, PendingClassifications);
    }

    private void SaveRules()
    {
        Interlocked.Increment(ref _version);
        Save(RulesKey, Rules);
    }

    private T? Load<T>(string key)
    {
        return Task.Run(() => _store.LoadAsync<T>(key)).GetAwaiter().GetResult();
    }

    private void Save<T>(string key, T value)
    {
        _pendingSave = _pendingSave.ContinueWith(
            async _ =>
            {
                try
                {
                    await _store.SaveAsync(key, value);
                }
                catch (SqliteException)
                {
                    // Best effort; the in-memory state still applies until the app closes.
                }
            },
            TaskScheduler.Default).Unwrap();
    }
}
