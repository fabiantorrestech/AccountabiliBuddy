using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Tasks;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// The weekly tidy-up (ADR 76, spec 05 section 9): the card's data (<see cref="Card"/>), the flow through the untouched Shelf tasks
/// (<see cref="Start"/>, <see cref="DecideAsync"/>, <see cref="FinishAsync"/>) and "not this week" (<see cref="DismissAsync"/>).
/// "Untouched" and the day come from the Tasks settings; the card shows only while no session runs. The week a tidy-up was finished or
/// dismissed in is kept in the document "tasks.tidyUp" (a date, this PC only). Decisions apply as they are made and can be changed
/// (each is re-applied from the task as it was when the tidy-up began). Use from the UI thread only.
/// </summary>
public sealed class TaskTidyUpService
{
    public const string DocumentKey = "tasks.tidyUp";

    private readonly TaskStore _store;
    private readonly TaskActionsService _actions;
    private readonly TaskSettingsService _settings;
    private readonly SessionController _sessions;
    private readonly SqliteJsonDocumentStore _documents;
    private readonly TimeProvider _time;
    private DateOnly? _handledFor;

    public TaskTidyUpService(
        TaskStore store, TaskActionsService actions, TaskSettingsService settings, SessionController sessions, SqliteJsonDocumentStore documents, TimeProvider time)
    {
        _store = store;
        _actions = actions;
        _settings = settings;
        _sessions = sessions;
        _documents = documents;
        _time = time;
    }

    /// <summary>The running tidy-up; null while none runs.</summary>
    public TidyUpPlan? Plan { get; private set; }

    /// <summary>Raised when the card or the plan differs.</summary>
    public event EventHandler? Changed;

    public void LoadBlocking()
    {
        try
        {
            _handledFor = Task.Run(() => _documents.LoadAsync<Handled>(DocumentKey)).GetAwaiter().GetResult()?.Week;
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            _handledFor = null;
        }
    }

    /// <summary>The card to show on the Shelf, or null (off, not due, nothing untouched, a session runs, or a tidy-up is already open).</summary>
    public TidyUpCard? Card()
    {
        TaskSettings settings = _settings.Current;
        int weeks = TaskUntouched.ClampWeeks(settings.UntouchedWeeks);
        int count = TaskUntouched.Of(_store.Lists, weeks, _time.GetUtcNow()).Count;
        bool due = Plan is null && TaskTidyUp.IsDue(settings.TidyUpOn, Today(), settings.TidyUpDay, _handledFor, _sessions.State.IsActive, count);
        return due ? TaskTidyUp.Card(count, weeks, settings.TidyUpDay) : null;
    }

    /// <summary>Begins the tidy-up over the untouched tasks; null when none are untouched or a session runs.</summary>
    public TidyUpPlan? Start()
    {
        if (_sessions.State.IsActive)
        {
            return null;
        }

        Plan = TaskTidyUp.Start(_store.Lists, TaskUntouched.ClampWeeks(_settings.Current.UntouchedWeeks), _time.GetUtcNow(), Today());
        Changed?.Invoke(this, EventArgs.Empty);
        return Plan;
    }

    /// <summary>Chooses for the task at <paramref name="index"/>; it applies at once, from the task as it was when the tidy-up began.</summary>
    public async Task DecideAsync(int index, TidyUpChoice choice)
    {
        if (Plan is not TidyUpPlan plan || index < 0 || index >= plan.Total)
        {
            return;
        }

        TidyUpItem item = plan.Items[index];
        await _store.ChangeAsync(null, (lists, self, now) => TaskTidyUp.Apply(lists, item, choice, now, self).Lists);
        Plan = plan.With(index, choice);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The tidy-up is done ("Tidied 4 of 5 tasks"): the week is handled and the card goes until the next chosen day.</summary>
    public async Task FinishAsync()
    {
        if (Plan is not TidyUpPlan plan)
        {
            return;
        }

        Plan = null;
        _actions.Announce(TaskToasts.Tidied(plan.Decided, plan.Total), canUndo: false);
        await MarkHandledAsync();
    }

    /// <summary>"Stop for now": the tidy-up closes, what was decided stays, and the card stays for the rest of the week.</summary>
    public void Stop()
    {
        Plan = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The card's ×, "not this week": it comes back on the next chosen day, and only if untouched tasks remain.</summary>
    public Task DismissAsync()
    {
        return MarkHandledAsync();
    }

    private async Task MarkHandledAsync()
    {
        _handledFor = TaskTidyUp.PeriodStart(Today(), _settings.Current.TidyUpDay);
        try
        {
            await _documents.SaveAsync(DocumentKey, new Handled(_handledFor));
        }
        catch (SqliteException)
        {
            // Not remembered across a restart this time; nothing else depends on it.
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private DateOnly Today()
    {
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_time.GetUtcNow(), TimeZoneInfo.Local).DateTime);
    }

    private sealed record Handled(DateOnly? Week);
}
