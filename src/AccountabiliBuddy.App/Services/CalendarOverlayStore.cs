using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// What this computer keeps so calendar events can shape the day plan's guarded windows (ADR 58), in the documents table and
/// never in the shared profile JSON:
/// <list type="bullet">
/// <item>"calendar.classifications": the category chosen on an event or its series ("Don't classify" included).</item>
/// <item>"calendar.rules": the per-calendar rules in force and a changed set waiting to take over.</item>
/// <item>"calendar.firstKnown": when each classified event was first seen at its current time and category (the notice
/// a looser change needs). Not backed up or restored: restoring it would hand out notice that was never served.</item>
/// </list>
/// Reads are snapshots of immutable lists and may come from any thread; changes are made under a lock and saved one after
/// another so an older state never overwrites a newer one.
/// </summary>
public sealed class CalendarOverlayStore
{
    public const string ClassificationsKey = "calendar.classifications";
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
            Rules = Load<CalendarRules>(RulesKey) ?? CalendarRules.Empty;
            FirstKnown = Load<List<FirstKnownEntry>>(FirstKnownKey) ?? (IReadOnlyList<FirstKnownEntry>)Array.Empty<FirstKnownEntry>();
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            // Unreadable data: start with nothing classified, which leaves the plan exactly as stored.
        }
    }

    /// <summary>
    /// Chooses a category for one event or its whole series; a null <paramref name="categoryId"/> is "Don't classify". A
    /// series choice on an event that doesn't repeat is stored as a choice for that event.
    /// </summary>
    public void SetClassification(CalendarEvent calendarEvent, ClassificationScope scope, Guid? categoryId)
    {
        EventClassification classification = ClassificationFor(calendarEvent, scope, categoryId);
        lock (_gate)
        {
            Classifications = Classifications
                .Where(existing => !SameTarget(existing, classification))
                .Append(classification)
                .ToList();
            SaveClassifications();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Takes back a choice, so the rules (or the plan) decide again.</summary>
    public void RemoveClassification(CalendarEvent calendarEvent, ClassificationScope scope)
    {
        EventClassification target = ClassificationFor(calendarEvent, scope, null);
        lock (_gate)
        {
            List<EventClassification> kept = Classifications.Where(existing => !SameTarget(existing, target)).ToList();
            if (kept.Count == Classifications.Count)
            {
                return;
            }

            Classifications = kept;
            SaveClassifications();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Schedules <paramref name="set"/> to replace the active rules at <paramref name="takesOverAt"/> (a rule change is a
    /// looser change); a set already waiting is replaced and the wait starts over. The caller decides whether a session
    /// forbids it (<see cref="EffectiveDayPlanProvider.TrySaveRules"/>).
    /// </summary>
    public void SchedulePendingRules(CalendarRuleSet set, DateTimeOffset takesOverAt)
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

    private static EventClassification ClassificationFor(CalendarEvent calendarEvent, ClassificationScope scope, Guid? categoryId)
    {
        bool series = scope == ClassificationScope.WholeSeries && calendarEvent.SeriesId is not null;
        return series
            ? new EventClassification(calendarEvent.CalendarId, calendarEvent.SeriesId!, ClassificationScope.WholeSeries, categoryId)
            : new EventClassification(calendarEvent.CalendarId, calendarEvent.ClassificationKey, ClassificationScope.ThisEvent, categoryId);
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
