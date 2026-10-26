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
/// midnight still gets its card), on days with work parts only, up to 6 hours after the work ended. Use from the UI thread only.
/// </summary>
public sealed class ShutdownService : IDisposable
{
    private const string DocumentKey = "shutdown";

    // A day's card still comes this long after its work ended (e.g. the app was closed then); later it's skipped.
    private static readonly TimeSpan LateLimit = TimeSpan.FromHours(6);

    private readonly SessionController _sessions;
    private readonly ConfigurationService _configuration;
    private readonly SessionTasksService _tasks;
    private readonly ComebackStore _comebacks;
    private readonly SqliteJsonDocumentStore _store;
    private ShutdownState _state = new(null);

    public ShutdownService(
        SessionController sessions, ConfigurationService configuration, SessionTasksService tasks, ComebackStore comebacks, SqliteJsonDocumentStore store)
    {
        _sessions = sessions;
        _configuration = configuration;
        _tasks = tasks;
        _comebacks = comebacks;
        _store = store;
        _sessions.Ticked += OnTicked;
    }

    /// <summary>The card showing on Today, or null.</summary>
    public ShutdownCardViewModel? Current { get; private set; }

    /// <summary>Raised when <see cref="Current"/> opens or closes.</summary>
    public event EventHandler? Changed;

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

    /// <summary>Opens the card once the day's work is over. Runs every second.</summary>
    public void CheckNow()
    {
        if (Current is not null || _sessions.State.IsActive)
        {
            return;
        }

        DateTime now = TimeZoneInfo.ConvertTime(_sessions.Now, _sessions.TimeProvider.LocalTimeZone).DateTime;
        DateOnly today = DateOnly.FromDateTime(now);
        // Yesterday's too: a last work part that ends at or after midnight belongs to the day it started on.
        foreach (DateOnly date in new[] { today.AddDays(-1), today })
        {
            if ((_state.LastShownOn is DateOnly shown && shown >= date) || WorkEnd(date) is not DateTime end || now < end || now - end > LateLimit)
            {
                continue;
            }

            Open(date);
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
        DateTime start = date.ToDateTime(TimeOnly.MinValue);
        List<PlannedFocus> work = DayTimeline.Planned(_configuration.Current.DayPlan, new LocalWindow(start, start.AddDays(2)))
            .Where(part => DateOnly.FromDateTime(part.Window.Start) == date)
            .ToList();
        return work.Count == 0 ? null : work.Max(part => part.Window.End);
    }

    private void Open(DateOnly date)
    {
        _state = new ShutdownState(date);
        _ = SaveAsync();
        List<string> waiting = _tasks.Current.Tasks.Select(task => task.Title).ToList();
        List<ShutdownTaskRow> unfinished = _tasks.History
            .Where(checklist => checklist.SessionStart is DateTimeOffset start
                && DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, _sessions.TimeProvider.LocalTimeZone).DateTime) == date)
            .SelectMany(checklist => checklist.Tasks.Where(task => !task.IsDone))
            .DistinctBy(task => task.Title, StringComparer.Ordinal)
            .Where(task => !waiting.Contains(task.Title))
            .Select(task => new ShutdownTaskRow(task.Title, ExistingCategory(task.CategoryId)))
            .ToList();
        List<ShutdownComebackRow> comebacks = _comebacks.Open.Select(comeback => new ShutdownComebackRow(comeback.Id, comeback.Text)).ToList();
        Current = new ShutdownCardViewModel(comebacks, unfinished, ApplyAsync, CloseAsync);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task ApplyAsync(ShutdownCardViewModel card)
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

        foreach (ShutdownTaskRow row in card.Unfinished.Where(row => row.Carry))
        {
            await _tasks.AddAsync(row.Title, null, ExistingCategory(row.CategoryId));
        }

        await CloseAsync();
    }

    // A carried task keeps its category only while that category still exists.
    private Guid? ExistingCategory(Guid? categoryId)
    {
        return _tasks.Categories.Any(category => category.Id == categoryId) ? categoryId : null;
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
