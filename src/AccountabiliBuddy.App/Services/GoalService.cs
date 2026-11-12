using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>Loads and saves the daily focus goal (documents key "goal"). Use from the UI thread only.</summary>
public sealed class GoalService
{
    private const string DocumentKey = "goal";

    private readonly SqliteJsonDocumentStore _store;

    public GoalService(SqliteJsonDocumentStore store)
    {
        _store = store;
    }

    public GoalSettings Current { get; private set; } = GoalSettings.Default;

    /// <summary>
    /// A daily goal was chosen on this PC (the "goal" document holds one, or <see cref="SetAsync"/> ran); false on a first run, where
    /// <see cref="Current"/> is only the default (milestone review NIT1: a first choice isn't a loosening).
    /// </summary>
    public bool HasChoice { get; private set; }

    /// <summary>Raised after the goal changed.</summary>
    public event EventHandler? Changed;

    public void LoadBlocking()
    {
        try
        {
            GoalSettings? loaded = Task.Run(() => _store.LoadAsync<GoalSettings>(DocumentKey)).GetAwaiter().GetResult();
            Current = loaded is { DailyMinutes: > 0 } ? loaded : GoalSettings.Default;
            HasChoice = loaded is { DailyMinutes: > 0 };
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Current = GoalSettings.Default;
        }
    }

    public async Task SetAsync(int dailyMinutes)
    {
        Current = new GoalSettings(Math.Max(1, dailyMinutes));
        HasChoice = true;
        Changed?.Invoke(this, EventArgs.Empty);
        await _store.SaveAsync(DocumentKey, Current);
    }
}
