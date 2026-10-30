using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The shutdown routine (redesign phase 5, design J): once the day's last work part is over and no session runs, one
/// card on Today with the open come-backs, the tasks left open in today's sessions and a box for tomorrow's first task.
/// It shows once per plan date at most (documents key "shutdown": the date whose work it wraps up, so work ending after
/// midnight still gets its card), on days with work parts only, up to 6 hours after the work ended. Left alone, the card
/// closes by itself 6 hours after the work ended or when the next day's first work part starts, whichever comes first, so
/// Today's Start comes back; nothing on it is applied then, except a typed tomorrow's first task, which goes to the Tasks
/// page's "Auto-saved" (<see cref="AutoSavedTaskStore"/>). Use from the UI thread only.
/// </summary>
public sealed class ShutdownService : IDisposable
{
    private const string DocumentKey = "shutdown";

    // A day's card still comes this long after its work ended (e.g. the app was closed then); later it's skipped, and a
    // card left open closes by itself.
    private static readonly TimeSpan LateLimit = TimeSpan.FromHours(6);

    private readonly SessionController _sessions;
    private readonly ConfigurationService _configuration;
    private readonly SessionTasksService _tasks;
    private readonly ComebackStore _comebacks;
    private readonly SqliteJsonDocumentStore _store;
    private readonly AutoSavedTaskStore _autoSaved;
    private ShutdownState _state = new(null);

    // The plan date the open card wraps up, and when it closes by itself.
    private DateOnly _openFor;
    private DateTime _closesAt;

    public ShutdownService(
        SessionController sessions, ConfigurationService configuration, SessionTasksService tasks, ComebackStore comebacks,
        SqliteJsonDocumentStore store, AutoSavedTaskStore autoSaved)
    {
        _sessions = sessions;
        _configuration = configuration;
        _tasks = tasks;
        _comebacks = comebacks;
        _store = store;
        _autoSaved = autoSaved;
        _sessions.Ticked += OnTicked;
    }

    /// <summary>Counted focus on a date, for the day plan's Budget days (a met budget ends the day's work windows). Set by the app.</summary>
    public Func<DateOnly, TimeSpan>? FocusedOn { get; set; }

    /// <summary>The night before a date from the sleep settings, for a Budget day whose plan has no Sleep part. Set by the app.</summary>
    public Func<DateOnly, SleepTimes?>? NightFallback { get; set; }

    /// <summary>The card showing on Today, or null.</summary>
    public ShutdownCardViewModel? Current { get; private set; }

    /// <summary>Raised when <see cref="Current"/> opens or closes.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when a card that closed by itself kept its tomorrow's first task under the Tasks page's "Auto-saved".</summary>
    public event EventHandler? AutoSaved;

    public void LoadBlocking()
    {
        try
        {
            _state = Task.Run(() => _store.LoadAsync<ShutdownState>(DocumentKey)).GetAwaiter().GetResult() ?? new ShutdownState(null);
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            _state = new ShutdownState(null);
        }
    }

    /// <summary>Opens the card once the day's work is over, and closes one left open too long. Runs every second.</summary>
    public void CheckNow()
    {
        DateTime now = TimeZoneInfo.ConvertTime(_sessions.Now, _sessions.TimeProvider.LocalTimeZone).DateTime;
        // Even while a session runs: the card is only hidden then.
        if (Current is not null && now >= _closesAt)
        {
            AutoClose();
        }

        if (Current is not null || _sessions.State.IsActive)
        {
            return;
        }

        DateOnly today = DateOnly.FromDateTime(now);
        // Yesterday's too: a last work part that ends at or after midnight belongs to the day it started on.
        foreach (DateOnly date in new[] { today.AddDays(-1), today })
        {
            if ((_state.LastShownOn is DateOnly shown && shown >= date) || WorkEnd(date) is not DateTime end || now < end || now - end > LateLimit)
            {
                continue;
            }

            Open(date, end);
            return;
        }
    }

    public void Dispose()
    {
        _sessions.Ticked -= OnTicked;
    }

    // When the last work part that starts on [date] ends; null on a day without work parts.
    private DateTime? WorkEnd(DateOnly date)
    {
        List<PlannedFocus> work = WorkStartingOn(date);
        return work.Count == 0 ? null : work.Max(part => part.Window.End);
    }

    // When the first work part that starts on [date] starts; null on a day without work parts.
    private DateTime? FirstWorkStart(DateOnly date)
    {
        List<PlannedFocus> work = WorkStartingOn(date);
        return work.Count == 0 ? null : work.Min(part => part.Window.Start);
    }

    // The work parts that start on [date] (one may run past midnight).
    private List<PlannedFocus> WorkStartingOn(DateOnly date)
    {
        DateTime start = date.ToDateTime(TimeOnly.MinValue);
        DateTime now = TimeZoneInfo.ConvertTime(_sessions.Now, _sessions.TimeProvider.LocalTimeZone).DateTime;
        return DayWorkWindows.WorkWindows(_configuration.Current.DayPlan, new LocalWindow(start, start.AddDays(2)), now, FocusedOn, NightFallback)
            .Where(part => DateOnly.FromDateTime(part.Window.Start) == date)
            .ToList();
    }

    private void Open(DateOnly date, DateTime workEnd)
    {
        _openFor = date;
        DateTime lateEnd = workEnd + LateLimit;
        _closesAt = FirstWorkStart(date.AddDays(1)) is DateTime nextWork && nextWork > workEnd && nextWork < lateEnd ? nextWork : lateEnd;
        _state = new ShutdownState(date);
        _ = SaveAsync();
        List<string> waiting = _tasks.Current.Tasks.Select(task => task.Title).ToList();
        List<ShutdownTaskRow> unfinishedRows = _tasks.History
            .Where(checklist => checklist.SessionStart is DateTimeOffset start
                && DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, _sessions.TimeProvider.LocalTimeZone).DateTime) == date)
            .SelectMany(checklist => checklist.Tasks.Where(task => !task.IsDone && !_tasks.WasMoved(task.Id)))
            .DistinctBy(task => task.Title, StringComparer.Ordinal)
            .Where(task => !waiting.Contains(task.Title))
            .Select(task => new ShutdownTaskRow(task.Title, ExistingCategory(task.CategoryId)))
            .ToList();
        List<ShutdownTaskGroup> unfinished = GroupByCategory(unfinishedRows);
        List<ShutdownComebackRow> comebacks = _comebacks.Open.Select(comeback => new ShutdownComebackRow(comeback.Id, comeback.Text)).ToList();
        Current = new ShutdownCardViewModel(comebacks, unfinished, card => ApplyAsync(card, date), CloseAsync);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task ApplyAsync(ShutdownCardViewModel card, DateOnly date)
    {
        foreach (ShutdownComebackRow row in card.Comebacks.Where(row => row.IsDone))
        {
            await _comebacks.TickAsync(row.Id);
        }

        string tomorrow = card.TomorrowTask.Trim();
        if (tomorrow.Length > 0)
        {
            // Tomorrow's first task goes to the top of the checklist.
            await _tasks.AddFirstAsync(tomorrow);
        }

        foreach (ShutdownTaskRow row in card.Unfinished.Where(row => !row.IsDone))
        {
            await _tasks.AddAsync(row.Title, null, ExistingCategory(row.CategoryId));
        }

        HashSet<string> ticked = card.Unfinished.Where(row => row.IsDone).Select(row => row.Title).ToHashSet(StringComparer.Ordinal);
        if (ticked.Count > 0)
        {
            await _tasks.MarkHistoryDoneAsync(date, ticked);
        }

        await CloseAsync();
    }

    // Categories in their own order, then the rows with no category last.
    private List<ShutdownTaskGroup> GroupByCategory(IReadOnlyList<ShutdownTaskRow> rows)
    {
        List<ShutdownTaskGroup> groups = _tasks.Categories
            .Select(category => new ShutdownTaskGroup(
                category.Name, TaskCategoryBrushes.Of(category.Color), true, rows.Where(row => row.CategoryId == category.Id).ToList()))
            .Where(group => group.Rows.Count > 0)
            .ToList();
        List<ShutdownTaskRow> uncategorised = rows.Where(row => row.CategoryId is null).ToList();
        if (uncategorised.Count > 0)
        {
            groups.Add(new ShutdownTaskGroup("No category", TaskCategoryBrushes.Of(null), false, uncategorised));
        }

        return groups;
    }

    // A carried task keeps its category only while that category still exists.
    private Guid? ExistingCategory(Guid? categoryId)
    {
        return _tasks.Categories.Any(category => category.Id == categoryId) ? categoryId : null;
    }

    // The card was left open too long: it closes like Skip, but a typed tomorrow's first task is kept under "Auto-saved".
    private void AutoClose()
    {
        string tomorrow = Current!.TomorrowTask.Trim();
        _ = CloseAsync();
        if (tomorrow.Length > 0)
        {
            _ = KeepAutoSavedAsync(tomorrow, _openFor);
            AutoSaved?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task KeepAutoSavedAsync(string title, DateOnly wrapUpOf)
    {
        try
        {
            await _autoSaved.AddAsync(title, wrapUpOf);
        }
        catch (SqliteException)
        {
            // Best effort: the item still shows until the app restarts.
        }
    }

    private Task CloseAsync()
    {
        Current = null;
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    private async Task SaveAsync()
    {
        try
        {
            await _store.SaveAsync(DocumentKey, _state);
        }
        catch (SqliteException)
        {
            // Best effort: at worst the card shows once more after a restart.
        }
    }

    private void OnTicked(object? sender, EventArgs e)
    {
        CheckNow();
    }
}
