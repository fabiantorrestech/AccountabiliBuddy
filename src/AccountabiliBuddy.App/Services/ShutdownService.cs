using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The shutdown routine (redesign phase 5, design J): once the day's last work part is over and no session runs, one
/// card on Today with the open come-backs, the tasks left open in today's sessions and a box for tomorrow's first task.
/// It shows once a day at most (documents key "shutdown"), on days with work parts only. Use from the UI thread only.
/// </summary>
public sealed class ShutdownService : IDisposable
{
    private const string DocumentKey = "shutdown";

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
        DateTime now = TimeZoneInfo.ConvertTime(_sessions.Now, _sessions.TimeProvider.LocalTimeZone).DateTime;
        DateOnly today = DateOnly.FromDateTime(now);
        if (Current is not null || _state.LastShownOn == today || _sessions.State.IsActive)
        {
            return;
        }

        var day = new LocalWindow(now.Date, now.Date.AddDays(1));
        IReadOnlyList<PlannedFocus> work = DayTimeline.Planned(_configuration.Current.DayPlan, day);
        if (work.Count == 0 || now < work.Max(part => part.Window.End))
        {
            return;
        }

        _state = new ShutdownState(today);
        _ = SaveAsync();
        List<string> waiting = _tasks.Current.Tasks.Select(task => task.Title).ToList();
        List<ShutdownTaskRow> unfinished = _tasks.History
            .Where(checklist => checklist.SessionStart is DateTimeOffset start
                && DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, _sessions.TimeProvider.LocalTimeZone).DateTime) == today)
            .SelectMany(checklist => checklist.Tasks.Where(task => !task.IsDone).Select(task => task.Title))
            .Distinct(StringComparer.Ordinal)
            .Where(title => !waiting.Contains(title))
            .Select(title => new ShutdownTaskRow(title))
            .ToList();
        List<ShutdownComebackRow> comebacks = _comebacks.Open.Select(comeback => new ShutdownComebackRow(comeback.Id, comeback.Text)).ToList();
        Current = new ShutdownCardViewModel(comebacks, unfinished, ApplyAsync, CloseAsync);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _sessions.Ticked -= OnTicked;
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
            await _tasks.AddAsync(tomorrow, null);
            // Tomorrow's first task goes to the top of the checklist.
            int index = _tasks.Current.Tasks.Count - 1;
            if (index > 0)
            {
                await _tasks.MoveAsync(_tasks.Current.Tasks[index].Id, -index);
            }
        }

        foreach (ShutdownTaskRow row in card.Unfinished.Where(row => row.Carry))
        {
            await _tasks.AddAsync(row.Title, null);
        }

        await CloseAsync();
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
