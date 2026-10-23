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

    public void LoadBlocking()
    {
        try
        {
            GoalSettings? loaded = Task.Run(() => _store.LoadAsync<GoalSettings>(DocumentKey)).GetAwaiter().GetResult();
            Current = loaded is { DailyMinutes: > 0 } ? loaded : GoalSettings.Default;
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Current = GoalSettings.Default;
        }
    }

    public async Task SetAsync(int dailyMinutes)
    {
        Current = new GoalSettings(Math.Max(1, dailyMinutes));
        await _store.SaveAsync(DocumentKey, Current);
    }
}
