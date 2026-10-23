using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Learn mode (redesign phase 2): on a fresh install, the first session asks about nothing new; what it used is settled on
/// the review card at its end. Begun only on the first run (the setup assistant never shown before), so a computer set up
/// earlier never gets it; used up when that session ends (documents key "learn.firstSession"). Use from the UI thread only.
/// </summary>
public sealed class LearnModeService
{
    private const string DocumentKey = "learn.firstSession";

    private readonly SqliteJsonDocumentStore _store;

    public LearnModeService(SqliteJsonDocumentStore store)
    {
        _store = store;
    }

    /// <summary>The next session the user starts learns.</summary>
    public bool IsPending { get; private set; }

    public void LoadBlocking()
    {
        try
        {
            IsPending = Task.Run(() => _store.LoadAsync<LearnModeState>(DocumentKey)).GetAwaiter().GetResult() is { Pending: true };
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            IsPending = false;
        }
    }

    /// <summary>On the first run only: the first session will learn.</summary>
    /// <param name="assistantShownBefore">The setup assistant has run on this computer before (set up earlier).</param>
    public async Task BeginIfFirstRunAsync(bool assistantShownBefore)
    {
        if (assistantShownBefore || IsPending)
        {
            return;
        }

        IsPending = true;
        await SaveAsync();
    }

    /// <summary>The learning session ended.</summary>
    public async Task FinishAsync()
    {
        if (!IsPending)
        {
            return;
        }

        IsPending = false;
        await SaveAsync();
    }

    private async Task SaveAsync()
    {
        try
        {
            await _store.SaveAsync(DocumentKey, new LearnModeState(IsPending));
        }
        catch (SqliteException)
        {
            // Best effort: at worst the first session asks as usual, or learns once more.
        }
    }
}
